using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class InventoryMigrationStatus
    {
        public bool Running { get; set; }
        public int LegacyItemCount { get; set; }
        public int LegacyEquippedCount { get; set; }
        public int LegacyAccountCargoCount { get; set; }
        public int NewItemCount { get; set; }
        public int NewAccountCargoCount { get; set; }
        public bool CanUpgrade => !Running && LegacyItemCount + LegacyEquippedCount + LegacyAccountCargoCount > 0;
        public bool CanDowngrade => !Running && NewItemCount + NewAccountCargoCount > 0;
    }

    public sealed class InventoryMigrationResidual
    {
        public int CharacterId { get; set; }
        public string CharacterName { get; set; }
        public int AccountId { get; set; }
        public string BagType { get; set; }
        public int ItemCount { get; set; }
        public int RequiredFreeSlots { get; set; }
        public string Reason { get; set; }
    }

    public sealed class InventoryMigrationReport
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Direction { get; set; }
        public int MigratedItems { get; set; }
        public int MigratedCharacters { get; set; }
        public List<object> CompleteCharacters { get; } = new List<object>();
        public List<InventoryMigrationResidual> Residuals { get; } = new List<InventoryMigrationResidual>();
        public InventoryMigrationStatus Status { get; set; }
    }

    /// <summary>
    /// 旧/新版离线数据双向迁移。一个方向的一次执行只开启一个 SQLite 事务；
    /// 源侧容量不足数据保留，其余成功项提交，任何异常则整个事务回滚。
    /// </summary>
    public sealed class InventoryDataMigrationCoordinator
    {
        private static readonly SemaphoreSlim MigrationLock = new SemaphoreSlim(1, 1);
        private static readonly int[] TitleBookCapacities = { 80, 170, 50, 100, 100 };
        private readonly string _connectionString;
        private readonly Func<ItemCore, int?> _stackLimitResolver;

        public InventoryDataMigrationCoordinator(string connectionString)
            : this(connectionString, null)
        {
        }

        internal InventoryDataMigrationCoordinator(string connectionString, Func<ItemCore, int?> stackLimitResolver)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _stackLimitResolver = stackLimitResolver ?? ResolveServerStackLimit;
        }

        public InventoryMigrationStatus GetStatus()
        {
            using var connection = OpenConnection();
            return ReadStatus(connection, MigrationLock.CurrentCount == 0);
        }

        public InventoryMigrationReport MigrateLegacyToNew()
            => Run("legacy-to-new", MigrateLegacyToNewCore);

        public InventoryMigrationReport MigrateNewToLegacy()
            => Run("new-to-legacy", MigrateNewToLegacyCore);

        private InventoryMigrationReport Run(string direction, Action<SqliteConnection, SqliteTransaction, InventoryMigrationReport, HashSet<int>> action)
        {
            if (!MigrationLock.Wait(0))
                return new InventoryMigrationReport { Success = false, Error = "已有背包迁移事务正在执行，请等待完成", Direction = direction, Status = GetStatus() };
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                var report = new InventoryMigrationReport { Direction = direction };
                var touchedCharacters = new HashSet<int>();
                action(connection, transaction, report, touchedCharacters);
                PopulateCharacterReport(connection, transaction, report, touchedCharacters);
                transaction.Commit();
                report.Success = true;
                report.Status = ReadStatus(connection, running: false);
                return report;
            }
            finally
            {
                MigrationLock.Release();
            }
        }

        private void MigrateLegacyToNewCore(SqliteConnection connection, SqliteTransaction transaction, InventoryMigrationReport report, HashSet<int> touched)
        {
            var occupied = LoadNewOccupied(connection, transaction);

            foreach (var equipped in LegacyItemCoreConverter.ReadEquippedRows(connection, transaction).OrderBy(x => x.CharacterId).ThenBy(x => x.SlotIndex))
            {
                touched.Add(equipped.CharacterId);
                if (equipped.SlotIndex == 28)
                {
                    // 名称装饰卡是单状态数据。新版已有状态时以新版为准，
                    // 旧槽同样视为已处理并清除，避免永远残留在旧穿戴表。
                    TryMigrateLegacyNameTag(connection, transaction, equipped);
                    DeleteLegacyEquipped(connection, transaction, equipped.CharacterId, equipped.SlotIndex);
                    report.MigratedItems++;
                    continue;
                }

                if (!ItemSlotBoundService.TryResolveItemKindForMigration(
                        InventoryListType.Equipment, equipped.SlotIndex, equipped.ItemTemplateId, out var itemKind))
                {
                    AddResidual(connection, transaction, report, equipped.CharacterId, InventoryListType.Equipment, equipped.SlotIndex, 1, "穿戴槽位无法映射到背包");
                    continue;
                }
                var fields = MakeEquipListCodec.ParseDisplayFields(equipped.RawEntry);
                var core = LegacyItemCoreConverter.BuildCoreFromEquippedEntry(equipped, itemKind, fields);
                if (!NewInventoryStore.TryFindFirstFreeCharacterBagSlot(
                        connection, transaction, equipped.CharacterId, itemKind,
                        out var targetList, out var start, out var slot, out var destinationError))
                {
                    AddResidual(connection, transaction, report, equipped.CharacterId, targetList, start, 1,
                        "穿戴物品无法卸下：" + destinationError);
                    continue;
                }
                if (core.ItemKind == ItemCore.KindAvatar)
                {
                    var uid = AllocateSequence(connection, transaction, "character_avatar_uid_sequence");
                    core.AvatarUid = checked((int)uid);
                    InsertAvatarDetail(connection, transaction, LegacyItemCoreConverter.BuildAvatarDetailFromEquippedEntry(equipped, uid, fields));
                }
                InsertNewCharacterItem(connection, transaction, equipped.CharacterId, targetList, slot, core);
                UpdateLock(connection, transaction, equipped.CharacterId, core.EquipmentLockId, targetList, slot);
                DeleteLegacyEquipped(connection, transaction, equipped.CharacterId, equipped.SlotIndex);
                MarkOccupied(occupied, equipped.CharacterId, targetList, slot);
                report.MigratedItems++;
            }

            var newCharacterTargets = LoadNewCharacterItems(connection, transaction);
            var legacyRows = LegacyItemCoreConverter.ReadCharacterItemRows(connection, transaction)
                .OrderBy(x => x.CharacterId).ThenBy(x => x.ListType).ThenBy(x => x.SlotIndex).ThenBy(x => x.ItemUid)
                .ToList();
            var migratedCubeRows = MigrateLegacyCubesToNew(connection, transaction, report, touched, legacyRows);
            foreach (var row in legacyRows.Where(x => !migratedCubeRows.Contains(x.ItemUid)))
            {
                touched.Add(row.CharacterId);
                if (row.ListType == InventoryListType.Main && (row.SlotIndex == 352 || row.SlotIndex == 353))
                {
                    AddResidual(connection, transaction, report, row.CharacterId, row.ListType, row.SlotIndex, 1, "新版保留槽位不接收物品");
                    continue;
                }
                var core = LegacyItemCoreConverter.BuildCoreFromCharacterItem(row);
                if (!TryResolveTargetRange(connection, transaction, row.CharacterId, row.ListType, row.SlotIndex, core.ItemKind, out var targetList, out var start, out var end))
                {
                    AddResidual(connection, transaction, report, row.CharacterId, row.ListType, row.SlotIndex, 1, "物品类型或槽位无法映射");
                    continue;
                }
                if (targetList == InventoryListType.Main && row.SlotIndex <= 2
                    && TryMergeNewVirtualSlot(connection, transaction, row.CharacterId, row.SlotIndex, core))
                {
                    DeleteLegacyCharacterItem(connection, transaction, row.ItemUid);
                    report.MigratedItems++;
                    continue;
                }
                if (InventoryStackRuleService.IsStackable(core))
                {
                    if (!TryMigrateStackableToNewCharacter(
                            connection, transaction, occupied, newCharacterTargets,
                            row.CharacterId, targetList, row.SlotIndex, start, end, core,
                            out var requiredFreeSlots, out var stackError))
                    {
                        AddResidual(connection, transaction, report, row.CharacterId, targetList, start,
                            Math.Max(1, core.Count), stackError, requiredFreeSlots: requiredFreeSlots);
                        continue;
                    }
                    DeleteLegacyCharacterItem(connection, transaction, row.ItemUid);
                    report.MigratedItems++;
                    continue;
                }
                var slot = AllocateTargetSlot(occupied, row.CharacterId, targetList, row.SlotIndex, start, end);
                if (slot < 0)
                {
                    AddResidual(connection, transaction, report, row.CharacterId, targetList, start, 1, "目标背包已满");
                    continue;
                }
                if (core.ItemKind == ItemCore.KindAvatar)
                {
                    var uid = AllocateSequence(connection, transaction, "character_avatar_uid_sequence");
                    core.AvatarUid = checked((int)uid);
                    InsertAvatarDetail(connection, transaction, LegacyItemCoreConverter.BuildAvatarDetailFromCharacterItem(row, core, uid));
                }
                InsertNewCharacterItem(connection, transaction, row.CharacterId, targetList, (short)slot, core);
                UpdateLock(connection, transaction, row.CharacterId, core.EquipmentLockId, targetList, (short)slot);
                DeleteLegacyCharacterItem(connection, transaction, row.ItemUid);
                MarkOccupied(occupied, row.CharacterId, targetList, (short)slot);
                report.MigratedItems++;
            }

            var cargoOccupied = LoadNewAccountCargoOccupied(connection, transaction);
            var newCargoTargets = LoadNewAccountCargoItems(connection, transaction);
            foreach (var row in LegacyItemCoreConverter.ReadAccountCargoItemRows(connection, transaction).OrderBy(x => x.AccountId).ThenBy(x => x.SlotIndex).ThenBy(x => x.ItemUid))
            {
                if (row.CharacterId > 0) touched.Add(row.CharacterId);
                var core = LegacyItemCoreConverter.BuildCoreFromAccountCargoItem(row);
                var cargoEnd = GetAccountCargoMigrationOpenEnd(connection, transaction, row.AccountId);
                if (InventoryStackRuleService.IsStackable(core))
                {
                    if (!TryMigrateStackableToNewCargo(
                            connection, transaction, cargoOccupied, newCargoTargets,
                            row.AccountId, row.CharacterId, row.SlotIndex, cargoEnd, core,
                            out var requiredFreeSlots, out var stackError))
                    {
                        AddResidual(connection, transaction, report, row.CharacterId, InventoryListType.AccountCargo, 0,
                            Math.Max(1, core.Count), stackError, row.AccountId, requiredFreeSlots);
                        continue;
                    }
                    DeleteLegacyAccountCargoItem(connection, transaction, row.ItemUid);
                    report.MigratedItems++;
                    continue;
                }
                if (row.SlotIndex >= 0 && row.SlotIndex <= cargoEnd
                    && HasMatchingNewAccountCargo(connection, transaction, row.AccountId, row.SlotIndex, row.ItemTemplateId))
                {
                    DeleteLegacyAccountCargoItem(connection, transaction, row.ItemUid);
                    report.MigratedItems++;
                    continue;
                }
                var slot = AllocateAccountCargoSlot(cargoOccupied, row.AccountId, row.SlotIndex, cargoEnd);
                if (slot < 0)
                {
                    AddResidual(connection, transaction, report, row.CharacterId, InventoryListType.AccountCargo, 0, 1, "账号仓库已满", row.AccountId);
                    continue;
                }
                InsertNewAccountCargoItem(connection, transaction, row.AccountId, row.CharacterId, (short)slot, core);
                DeleteLegacyAccountCargoItem(connection, transaction, row.ItemUid);
                MarkAccountCargoOccupied(cargoOccupied, row.AccountId, (short)slot);
                report.MigratedItems++;
            }
            MigrateLegacyTitleBooksToNew(connection, transaction, report, touched);
        }

        private void MigrateNewToLegacyCore(SqliteConnection connection, SqliteTransaction transaction, InventoryMigrationReport report, HashSet<int> touched)
        {
            var newOccupied = LoadNewOccupied(connection, transaction);
            foreach (var row in LoadNewCharacterItems(connection, transaction).Where(x => x.ListType == InventoryListType.Equipment).OrderBy(x => x.CharacterId).ThenBy(x => x.SlotIndex))
            {
                touched.Add(row.CharacterId);
                if (!NewInventoryStore.TryFindFirstFreeCharacterBagSlot(
                        connection, transaction, row.CharacterId, row.Core.ItemKind,
                        out var targetList, out var start, out var slot, out var destinationError))
                {
                    AddResidual(connection, transaction, report, row.CharacterId, targetList, start, 1,
                        "穿戴物品无法映射到新版背包：" + destinationError);
                    continue;
                }
                MoveNewCharacterItem(connection, transaction, row.ItemUid, targetList, slot);
                UpdateLock(connection, transaction, row.CharacterId, row.Core.EquipmentLockId, targetList, slot);
                MarkOccupied(newOccupied, row.CharacterId, targetList, slot);
            }

            var legacyOccupied = LoadLegacyOccupied(connection, transaction);
            var legacyCharacterTargets = LoadLegacyCharacterItems(connection, transaction);
            foreach (var row in LoadNewCharacterItems(connection, transaction).Where(x => x.ListType != InventoryListType.Equipment).OrderBy(x => x.CharacterId).ThenBy(x => x.ListType).ThenBy(x => x.SlotIndex).ThenBy(x => x.ItemUid))
            {
                touched.Add(row.CharacterId);
                if (!TryResolveTargetRange(connection, transaction, row.CharacterId, row.ListType, row.SlotIndex, row.Core.ItemKind, out var targetList, out var start, out var end))
                {
                    AddResidual(connection, transaction, report, row.CharacterId, row.ListType, row.SlotIndex, 1, "物品类型或槽位无法映射到旧版");
                    continue;
                }
                if (targetList == InventoryListType.Main && row.SlotIndex <= 2
                    && TryMergeLegacyVirtualSlot(connection, transaction, row.CharacterId, row.SlotIndex, row.Core))
                {
                    DeleteNewCharacterItem(connection, transaction, row);
                    report.MigratedItems++;
                    continue;
                }
                if (InventoryStackRuleService.IsStackable(row.Core))
                {
                    if (!TryMigrateStackableToLegacyCharacter(
                            connection, transaction, legacyOccupied, legacyCharacterTargets,
                            row.CharacterId, targetList, row.SlotIndex, start, end, row.Core,
                            out var requiredFreeSlots, out var stackError))
                    {
                        AddResidual(connection, transaction, report, row.CharacterId, targetList, start,
                            Math.Max(1, row.Core.Count), stackError, requiredFreeSlots: requiredFreeSlots);
                        continue;
                    }
                    DeleteNewCharacterItem(connection, transaction, row);
                    report.MigratedItems++;
                    continue;
                }
                var slot = AllocateTargetSlot(legacyOccupied, row.CharacterId, targetList, row.SlotIndex, start, end);
                if (slot < 0)
                {
                    AddResidual(connection, transaction, report, row.CharacterId, targetList, start, 1, "旧版背包已满");
                    continue;
                }
                InsertLegacyCharacterItem(connection, transaction, row.CharacterId, targetList, (short)slot, row.Core, row.AvatarDetail);
                UpdateLock(connection, transaction, row.CharacterId, row.Core.EquipmentLockId, targetList, (short)slot);
                DeleteNewCharacterItem(connection, transaction, row);
                MarkOccupied(legacyOccupied, row.CharacterId, targetList, (short)slot);
                report.MigratedItems++;
            }

            var legacyCargoOccupied = LoadLegacyAccountCargoOccupied(connection, transaction);
            var legacyCargoTargets = LoadLegacyAccountCargoItems(connection, transaction);
            foreach (var row in LoadNewAccountCargoItems(connection, transaction).OrderBy(x => x.AccountId).ThenBy(x => x.SlotIndex).ThenBy(x => x.ItemUid))
            {
                if (row.CharacterId > 0) touched.Add(row.CharacterId);
                var cargoEnd = GetAccountCargoMigrationOpenEnd(connection, transaction, row.AccountId);
                if (InventoryStackRuleService.IsStackable(row.Core))
                {
                    if (!TryMigrateStackableToLegacyCargo(
                            connection, transaction, legacyCargoOccupied, legacyCargoTargets,
                            row.AccountId, row.CharacterId, row.SlotIndex, cargoEnd, row.Core,
                            out var requiredFreeSlots, out var stackError))
                    {
                        AddResidual(connection, transaction, report, row.CharacterId, InventoryListType.AccountCargo, 0,
                            Math.Max(1, row.Core.Count), stackError, row.AccountId, requiredFreeSlots);
                        continue;
                    }
                    DeleteNewAccountCargoItem(connection, transaction, row.ItemUid);
                    report.MigratedItems++;
                    continue;
                }
                if (row.SlotIndex >= 0 && row.SlotIndex <= cargoEnd
                    && HasMatchingLegacyAccountCargo(connection, transaction, row.AccountId, row.SlotIndex, row.Core.ItemId))
                {
                    DeleteNewAccountCargoItem(connection, transaction, row.ItemUid);
                    report.MigratedItems++;
                    continue;
                }
                var slot = AllocateAccountCargoSlot(legacyCargoOccupied, row.AccountId, row.SlotIndex, cargoEnd);
                if (slot < 0)
                {
                    AddResidual(connection, transaction, report, row.CharacterId, InventoryListType.AccountCargo, 0, 1, "旧版账号仓库已满", row.AccountId);
                    continue;
                }
                InsertLegacyAccountCargoItem(connection, transaction, row.AccountId, row.CharacterId, (short)slot, row.Core);
                DeleteNewAccountCargoItem(connection, transaction, row.ItemUid);
                MarkAccountCargoOccupied(legacyCargoOccupied, row.AccountId, (short)slot);
                report.MigratedItems++;
            }

            MigrateNewNameTagsToLegacy(connection, transaction, report, touched);
            MigrateNewTitleBooksToLegacy(connection, transaction, report, touched);
        }

        private static void MigrateLegacyTitleBooksToNew(
            SqliteConnection connection,
            SqliteTransaction transaction,
            InventoryMigrationReport report,
            HashSet<int> touched)
        {
            var source = LoadLegacyTitleBooks(connection, transaction);
            var residual = new Dictionary<int, Dictionary<(int Category, short Slot), ItemCore>>();
            var occupied = LoadNewTitleBookOccupied(connection, transaction);
            foreach (var character in source.OrderBy(x => x.Key))
            {
                touched.Add(character.Key);
                foreach (var entry in character.Value.OrderBy(x => x.Key.Category).ThenBy(x => x.Key.Slot))
                {
                    var category = entry.Key.Category;
                    var slot = AllocateTitleBookSlot(occupied, character.Key, category, entry.Key.Slot);
                    if (slot < 0)
                    {
                        // 称号簿冲突采用新版优先：目标分类已满时直接清除旧来源，
                        // 不再将旧记录写回 residual blob。
                        report.MigratedItems++;
                        continue;
                    }
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = @"INSERT INTO character_new_titlebook(character_id,category,slot_index,item_core,updated_at)
VALUES(@cid,@category,@slot,@core,CURRENT_TIMESTAMP);";
                    insert.Parameters.AddWithValue("@cid", character.Key);
                    insert.Parameters.AddWithValue("@category", category);
                    insert.Parameters.AddWithValue("@slot", slot);
                    insert.Parameters.AddWithValue("@core", entry.Value.ToBytes());
                    insert.ExecuteNonQuery();
                    MarkTitleBookOccupied(occupied, character.Key, category, (short)slot);
                    report.MigratedItems++;
                }
            }
            RewriteLegacyTitleBooks(connection, transaction, residual);
        }

        private static void MigrateNewTitleBooksToLegacy(
            SqliteConnection connection,
            SqliteTransaction transaction,
            InventoryMigrationReport report,
            HashSet<int> touched)
        {
            var target = LoadLegacyTitleBooks(connection, transaction);
            var occupied = new Dictionary<(int CharacterId, int Category), HashSet<short>>();
            foreach (var character in target)
                foreach (var key in character.Value.Keys)
                    MarkTitleBookOccupied(occupied, character.Key, key.Category, key.Slot);

            var rows = new List<(int CharacterId, int Category, short Slot, ItemCore Core)>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT character_id,category,slot_index,item_core FROM character_new_titlebook ORDER BY character_id,category,slot_index;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var bytes = (byte[])reader[3];
                    if (bytes.Length != ItemCore.Size)
                        throw new InvalidOperationException("新版称号簿 ItemCore 长度无效");
                    rows.Add((reader.GetInt32(0), reader.GetInt32(1), checked((short)reader.GetInt32(2)), ItemCore.FromBytes(bytes)));
                }
            }

            foreach (var row in rows)
            {
                touched.Add(row.CharacterId);
                var slot = AllocateTitleBookSlot(occupied, row.CharacterId, row.Category, row.Slot);
                if (slot < 0)
                {
                    // 称号簿不是普通背包。目标分类已满时以目标侧为准，
                    // 直接清理被迁移侧，不能作为“清空背包槽位后重试”的残余上报。
                    Exec(connection, transaction,
                        "DELETE FROM character_new_titlebook WHERE character_id=@cid AND category=@category AND slot_index=@slot;",
                        ("@cid", row.CharacterId), ("@category", row.Category), ("@slot", row.Slot));
                    report.MigratedItems++;
                    continue;
                }
                PutTitleBookItem(target, row.CharacterId, row.Category, (short)slot, row.Core);
                MarkTitleBookOccupied(occupied, row.CharacterId, row.Category, (short)slot);
                Exec(connection, transaction,
                    "DELETE FROM character_new_titlebook WHERE character_id=@cid AND category=@category AND slot_index=@slot;",
                    ("@cid", row.CharacterId), ("@category", row.Category), ("@slot", row.Slot));
                report.MigratedItems++;
            }
            RewriteLegacyTitleBooks(connection, transaction, target);
        }

        private static Dictionary<int, Dictionary<(int Category, short Slot), ItemCore>> LoadLegacyTitleBooks(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            var result = new Dictionary<int, Dictionary<(int, short), ItemCore>>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT character_id,general,specific,pvp,despair,event FROM character_titlebook;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var characterId = reader.GetInt32(0);
                    for (var category = 0; category < TitleBookCapacities.Length; category++)
                    {
                        if (reader.IsDBNull(category + 1)) continue;
                        var blob = (byte[])reader[category + 1];
                        var width = blob.Length == TitleBookCapacities[category] * 84 ? 84 : LegacyTitleBookCoreCodec.RecordSize;
                        for (short slot = 0; slot < TitleBookCapacities[category]; slot++)
                        {
                            var offset = slot * width;
                            if (offset + width > blob.Length) break;
                            ItemCore core;
                            if (width == LegacyTitleBookCoreCodec.RecordSize)
                                core = LegacyTitleBookCoreCodec.DecodeRecord(blob, offset);
                            else
                            {
                                var normalized = new byte[LegacyTitleBookCoreCodec.RecordSize];
                                Buffer.BlockCopy(blob, offset, normalized, 0, width);
                                core = LegacyTitleBookCoreCodec.DecodeRecord(normalized, 0);
                            }
                            if (!core.IsEmpty) PutTitleBookItem(result, characterId, category, slot, core);
                        }
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT character_id,chunk_index,entries_blob FROM character_achievement_chunks WHERE entries_blob IS NOT NULL;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var characterId = reader.GetInt32(0);
                    var category = reader.GetInt32(1);
                    if (category < 0 || category >= TitleBookCapacities.Length) continue;
                    var blob = (byte[])reader[2];
                    for (var offset = 0; offset + LegacyTitleBookCoreCodec.ListEntrySize <= blob.Length; offset += LegacyTitleBookCoreCodec.ListEntrySize)
                    {
                        if (!LegacyTitleBookCoreCodec.TryDecodeListEntry(blob, offset, out var slot, out var core)
                            || core.IsEmpty || slot < 0 || slot >= TitleBookCapacities[category]) continue;
                        if (!result.TryGetValue(characterId, out var items) || !items.ContainsKey((category, slot)))
                            PutTitleBookItem(result, characterId, category, slot, core);
                    }
                }
            }
            return result;
        }

        private static void RewriteLegacyTitleBooks(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Dictionary<int, Dictionary<(int Category, short Slot), ItemCore>> data)
        {
            Exec(connection, transaction, "DELETE FROM character_achievement_chunks;");
            Exec(connection, transaction, "DELETE FROM character_titlebook;");
            foreach (var character in data.Where(x => x.Value.Count > 0).OrderBy(x => x.Key))
            {
                var blobs = TitleBookCapacities.Select(capacity => new byte[capacity * LegacyTitleBookCoreCodec.RecordSize]).ToArray();
                foreach (var item in character.Value)
                {
                    if (item.Key.Category < 0 || item.Key.Category >= blobs.Length
                        || item.Key.Slot < 0 || item.Key.Slot >= TitleBookCapacities[item.Key.Category]) continue;
                    var record = LegacyTitleBookCoreCodec.EncodeRecord(item.Key.Slot, item.Value);
                    Buffer.BlockCopy(record, 0, blobs[item.Key.Category], item.Key.Slot * LegacyTitleBookCoreCodec.RecordSize, record.Length);
                }
                Exec(connection, transaction, @"INSERT INTO character_titlebook
(character_id,format_version,general,specific,pvp,despair,event,updated_at)
VALUES(@cid,1,@g,@s,@p,@d,@e,CURRENT_TIMESTAMP);",
                    ("@cid", character.Key), ("@g", blobs[0]), ("@s", blobs[1]), ("@p", blobs[2]), ("@d", blobs[3]), ("@e", blobs[4]));
            }
        }

        private static Dictionary<(int CharacterId, int Category), HashSet<short>> LoadNewTitleBookOccupied(SqliteConnection connection, SqliteTransaction transaction)
        {
            var result = new Dictionary<(int, int), HashSet<short>>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT character_id,category,slot_index FROM character_new_titlebook;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) MarkTitleBookOccupied(result, reader.GetInt32(0), reader.GetInt32(1), checked((short)reader.GetInt32(2)));
            return result;
        }

        private static int AllocateTitleBookSlot(Dictionary<(int CharacterId, int Category), HashSet<short>> occupied, int characterId, int category, short preferred)
        {
            if (category < 0 || category >= TitleBookCapacities.Length) return -1;
            if (!occupied.TryGetValue((characterId, category), out var used)) used = new HashSet<short>();
            for (var slot = Math.Max(0, (int)preferred); slot < TitleBookCapacities[category]; slot++) if (!used.Contains((short)slot)) return slot;
            for (var slot = 0; slot < Math.Min(Math.Max(0, (int)preferred), TitleBookCapacities[category]); slot++) if (!used.Contains((short)slot)) return slot;
            return -1;
        }

        private static void MarkTitleBookOccupied(Dictionary<(int CharacterId, int Category), HashSet<short>> occupied, int characterId, int category, short slot)
        {
            if (!occupied.TryGetValue((characterId, category), out var used)) occupied[(characterId, category)] = used = new HashSet<short>();
            used.Add(slot);
        }

        private static void PutTitleBookItem(Dictionary<int, Dictionary<(int Category, short Slot), ItemCore>> data, int characterId, int category, short slot, ItemCore core)
        {
            if (!data.TryGetValue(characterId, out var items)) data[characterId] = items = new Dictionary<(int, short), ItemCore>();
            items[(category, slot)] = core;
        }

        private static bool TryResolveTargetRange(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            InventoryListType sourceList,
            short sourceSlot,
            byte itemKind,
            out InventoryListType targetList,
            out short start,
            out short end)
        {
            targetList = sourceList;
            start = end = 0;
            if (sourceList == InventoryListType.PersonalCargo)
            {
                start = 0;
                end = NewInventoryStore.GetPersonalCargoOpenEnd(connection, transaction, characterId);
                return true;
            }
            if (sourceList == InventoryListType.Avatar) { start = 0; end = 209; return true; }
            if (sourceList == InventoryListType.Pet)
            {
                if (itemKind == ItemCore.KindCreature) { start=0; end=139; return true; }
                if (itemKind == ItemCore.KindCreatureEquipment) { start=140; end=188; return true; }
                if (itemKind == ItemCore.KindCreatureConsumable) { start=189; end=239; return true; }
            }
            if (sourceList == InventoryListType.Main && sourceSlot >= 0 && sourceSlot <= 2) { start=sourceSlot; end=sourceSlot; return true; }
            if (sourceList == InventoryListType.Main && sourceSlot >= 3 && sourceSlot <= 8) { start=3; end=8; return true; }
            return TryGetDefaultRange(connection, transaction, characterId, itemKind, out targetList, out start, out end);
        }

        private static bool TryGetDefaultRange(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte kind,
            out InventoryListType list,
            out short start,
            out short end)
            => NewInventoryStore.TryGetCharacterOpenRange(
                connection, transaction, characterId, kind,
                out list, out start, out end, out _);

        private static Dictionary<(int CharacterId, InventoryListType List), HashSet<short>> LoadNewOccupied(SqliteConnection connection, SqliteTransaction transaction)
            => LoadOccupied(connection, transaction, "SELECT COALESCE(character_id,owner_id),list_type,slot_index FROM character_new_items WHERE owner_scope='character';");

        private static Dictionary<(int CharacterId, InventoryListType List), HashSet<short>> LoadLegacyOccupied(SqliteConnection connection, SqliteTransaction transaction)
            => LoadOccupied(connection, transaction, "SELECT COALESCE(character_id,owner_id),list_type,slot_index FROM character_items WHERE owner_scope='character';");

        private static Dictionary<(int CharacterId, InventoryListType List), HashSet<short>> LoadOccupied(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            var result = new Dictionary<(int, InventoryListType), HashSet<short>>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read()) MarkOccupied(result, reader.GetInt32(0), (InventoryListType)reader.GetInt32(1), checked((short)reader.GetInt32(2)));
            return result;
        }

        private static int AllocateTargetSlot(Dictionary<(int CharacterId, InventoryListType List), HashSet<short>> occupied, int characterId, InventoryListType list, short preferred, short start, short end)
        {
            if (!occupied.TryGetValue((characterId, list), out var slots)) slots = new HashSet<short>();
            var first = Math.Max((int)preferred, start);
            for (var slot = first; slot <= end; slot++) if (!slots.Contains((short)slot)) return slot;
            for (var slot = (int)start; slot < first; slot++) if (!slots.Contains((short)slot)) return slot;
            return -1;
        }

        private static void MarkOccupied(Dictionary<(int CharacterId, InventoryListType List), HashSet<short>> occupied, int characterId, InventoryListType list, short slot)
        {
            if (!occupied.TryGetValue((characterId, list), out var slots)) occupied[(characterId, list)] = slots = new HashSet<short>();
            slots.Add(slot);
        }

        private static Dictionary<int, HashSet<short>> LoadNewAccountCargoOccupied(SqliteConnection c, SqliteTransaction t) => LoadAccountCargoOccupied(c,t,"account_cargo_new_items");
        private static Dictionary<int, HashSet<short>> LoadLegacyAccountCargoOccupied(SqliteConnection c, SqliteTransaction t) => LoadAccountCargoOccupied(c,t,"account_cargo_items");

        private static Dictionary<int, HashSet<short>> LoadAccountCargoOccupied(SqliteConnection connection, SqliteTransaction transaction, string table)
        {
            var result = new Dictionary<int, HashSet<short>>();
            using var command = connection.CreateCommand(); command.Transaction=transaction; command.CommandText=$"SELECT account_id,slot_index FROM {table};";
            using var reader=command.ExecuteReader(); while(reader.Read()) MarkAccountCargoOccupied(result,reader.GetInt32(0),checked((short)reader.GetInt32(1)));
            return result;
        }

        private static int AllocateAccountCargoSlot(Dictionary<int,HashSet<short>> occupied,int accountId,short preferred,short end)
        {
            if (end < 0) return -1;
            if(!occupied.TryGetValue(accountId,out var slots)) slots=new HashSet<short>();
            var first=Math.Max(0,(int)preferred);
            for(var slot=first;slot<=end;slot++) if(!slots.Contains((short)slot)) return slot;
            for(var slot=0;slot<Math.Min(first,end+1);slot++) if(!slots.Contains((short)slot)) return slot;
            return -1;
        }

        private static short GetAccountCargoMigrationOpenEnd(SqliteConnection connection, SqliteTransaction transaction, int accountId)
        {
            using (var state = connection.CreateCommand())
            {
                state.Transaction = transaction;
                state.CommandText = "SELECT selection_key FROM account_cargo_state WHERE account_id=@aid;";
                state.Parameters.AddWithValue("@aid", accountId);
                var value = state.ExecuteScalar();
                if (value != null && value != DBNull.Value)
                {
                    var capacity = Math.Max(0, Math.Min(Convert.ToInt32(value, CultureInfo.InvariantCulture), 64));
                    return checked((short)(capacity - 1));
                }
            }

            // 旧库可能已有仓库物品却没有状态行。迁移时只保留已有数据实际占用到的范围，
            // 不把缺失状态误当成已开放 64 格。
            using var occupied = connection.CreateCommand();
            occupied.Transaction = transaction;
            occupied.CommandText = @"SELECT MAX(slot_index) FROM (
SELECT account_id,slot_index FROM account_cargo_items
UNION ALL
SELECT account_id,slot_index FROM account_cargo_new_items
) WHERE account_id=@aid;";
            occupied.Parameters.AddWithValue("@aid", accountId);
            var maximum = occupied.ExecuteScalar();
            return maximum == null || maximum == DBNull.Value
                ? (short)-1
                : checked((short)Math.Max(0, Math.Min(Convert.ToInt32(maximum, CultureInfo.InvariantCulture), 63)));
        }

        private static void MarkAccountCargoOccupied(Dictionary<int,HashSet<short>> occupied,int accountId,short slot)
        { if(!occupied.TryGetValue(accountId,out var slots)) occupied[accountId]=slots=new HashSet<short>(); slots.Add(slot); }

        private static List<NewInventoryItemRecord> LoadNewCharacterItems(SqliteConnection connection, SqliteTransaction transaction)
        {
            var details=LoadAllAvatarDetails(connection,transaction); var result=new List<NewInventoryItemRecord>();
            using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT item_uid,COALESCE(character_id,owner_id),list_type,slot_index,item_core FROM character_new_items WHERE owner_scope='character';";
            using var reader=command.ExecuteReader();while(reader.Read()){var core=ItemCore.FromBytes((byte[])reader[4]);result.Add(new NewInventoryItemRecord{ItemUid=reader.GetInt64(0),CharacterId=reader.GetInt32(1),ListType=(InventoryListType)reader.GetInt32(2),SlotIndex=checked((short)reader.GetInt32(3)),Core=core,AvatarDetail=core.ItemKind==ItemCore.KindAvatar&&details.TryGetValue(core.AvatarUid,out var d)?d:null});}return result;
        }

        private static List<NewInventoryItemRecord> LoadNewAccountCargoItems(SqliteConnection connection, SqliteTransaction transaction)
        {
            var result=new List<NewInventoryItemRecord>();using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT item_uid,account_id,COALESCE(character_id,0),slot_index,item_core FROM account_cargo_new_items;";using var reader=command.ExecuteReader();while(reader.Read())result.Add(new NewInventoryItemRecord{ItemUid=reader.GetInt64(0),AccountId=reader.GetInt32(1),CharacterId=reader.GetInt32(2),ListType=InventoryListType.AccountCargo,SlotIndex=checked((short)reader.GetInt32(3)),Core=ItemCore.FromBytes((byte[])reader[4])});return result;
        }

        private static Dictionary<int,AvatarDetail> LoadAllAvatarDetails(SqliteConnection connection,SqliteTransaction transaction)
        {var result=new Dictionary<int,AvatarDetail>();using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT item_uid,owner_id,character_id,item_id,expire_date,clear_avatar_id,jewel_socket,color1,color2,delete_date FROM character_avatar_detail;";using var reader=command.ExecuteReader();while(reader.Read()){var d=AvatarDetailCodec.ReadDetail(reader);result[checked((int)d.AvatarUid)]=d;}return result;}

        private static List<NewInventoryItemRecord> LoadLegacyCharacterItems(SqliteConnection connection, SqliteTransaction transaction)
            => LegacyItemCoreConverter.ReadCharacterItemRows(connection, transaction)
                .Select(row => new NewInventoryItemRecord
                {
                    ItemUid = row.ItemUid,
                    CharacterId = row.CharacterId,
                    ListType = row.ListType,
                    SlotIndex = row.SlotIndex,
                    Core = LegacyItemCoreConverter.BuildCoreFromCharacterItem(row),
                })
                .ToList();

        private static List<NewInventoryItemRecord> LoadLegacyAccountCargoItems(SqliteConnection connection, SqliteTransaction transaction)
            => LegacyItemCoreConverter.ReadAccountCargoItemRows(connection, transaction)
                .Select(row => new NewInventoryItemRecord
                {
                    ItemUid = row.ItemUid,
                    AccountId = row.AccountId,
                    CharacterId = row.CharacterId,
                    ListType = InventoryListType.AccountCargo,
                    SlotIndex = row.SlotIndex,
                    Core = LegacyItemCoreConverter.BuildCoreFromAccountCargoItem(row),
                })
                .ToList();

        private sealed class StackMergeAction
        {
            public NewInventoryItemRecord Target { get; set; }
            public int AddCount { get; set; }
        }

        private sealed class StackInsertAction
        {
            public short Slot { get; set; }
            public int Count { get; set; }
        }

        private sealed class StackMigrationPlan
        {
            public bool IsExactMirror { get; set; }
            public List<StackMergeAction> Merges { get; } = new List<StackMergeAction>();
            public List<StackInsertAction> Inserts { get; } = new List<StackInsertAction>();
        }

        private bool TryBuildStackMigrationPlan(
            ItemCore source,
            short preferred,
            short start,
            short end,
            IReadOnlyList<NewInventoryItemRecord> targets,
            ISet<short> occupied,
            out StackMigrationPlan plan,
            out int requiredFreeSlots,
            out string error)
        {
            plan = null;
            requiredFreeSlots = 0;
            error = null;
            if (!InventoryStackRuleService.IsStackable(source) || source.Count <= 0)
            {
                error = "可堆叠物品数量无效";
                return false;
            }

            var preferredTarget = preferred >= start && preferred <= end
                ? targets.FirstOrDefault(target => target.SlotIndex == preferred)
                : null;
            if (preferredTarget != null && AreEquivalentStackMirrors(source, preferredTarget.Core))
            {
                plan = new StackMigrationPlan { IsExactMirror = true };
                return true;
            }

            var stackLimit = _stackLimitResolver(source);
            if (!stackLimit.HasValue || stackLimit.Value <= 0)
            {
                requiredFreeSlots = 0;
                error = "无法从 PVF 读取物品堆叠上限";
                return false;
            }

            plan = new StackMigrationPlan();
            var limit = stackLimit.Value;
            var remaining = source.Count;
            foreach (var target in targets
                .Where(target => target.SlotIndex >= start && target.SlotIndex <= end
                    && target.Core != null
                    && target.Core.ItemId == source.ItemId
                    && InventoryStackRuleService.IsStackable(target.Core))
                .OrderBy(target => target.SlotIndex))
            {
                if (remaining <= 0)
                    break;
                var room = Math.Max(0, limit - Math.Max(0, target.Core.Count));
                if (room <= 0)
                    continue;
                var add = Math.Min(remaining, room);
                plan.Merges.Add(new StackMergeAction { Target = target, AddCount = add });
                remaining -= add;
            }

            if (remaining <= 0)
                return true;

            var perSlot = limit == int.MaxValue ? remaining : limit;
            var neededSlots = checked((remaining + (long)perSlot - 1L) / perSlot);
            var freeSlots = EnumerateSlots(preferred, start, end)
                .Where(slot => !occupied.Contains(slot))
                .ToArray();
            if (freeSlots.LongLength < neededSlots)
            {
                requiredFreeSlots = checked((int)(neededSlots - freeSlots.LongLength));
                error = "背包空槽不足，无法容纳按堆叠上限拆分后的物品";
                plan = null;
                return false;
            }

            for (var index = 0; remaining > 0; index++)
            {
                var count = Math.Min(remaining, perSlot);
                plan.Inserts.Add(new StackInsertAction { Slot = freeSlots[index], Count = count });
                remaining -= count;
            }
            return true;
        }

        private bool TryMigrateStackableToNewCharacter(
            SqliteConnection connection, SqliteTransaction transaction,
            Dictionary<(int CharacterId, InventoryListType List), HashSet<short>> occupied,
            List<NewInventoryItemRecord> targets,
            int characterId, InventoryListType list, short preferred, short start, short end, ItemCore source,
            out int requiredFreeSlots, out string error)
        {
            var targetItems = targets.Where(target => target.CharacterId == characterId && target.ListType == list).ToList();
            var slots = GetOccupiedSlots(occupied, characterId, list);
            if (!TryBuildStackMigrationPlan(source, preferred, start, end, targetItems, slots, out var plan, out requiredFreeSlots, out error))
                return false;
            if (plan.IsExactMirror)
                return true;
            ApplyNewStackMerges(connection, transaction, plan);
            foreach (var insert in plan.Inserts)
            {
                var core = source.Copy();
                core.Count = insert.Count;
                var uid = InsertNewCharacterItem(connection, transaction, characterId, list, insert.Slot, core);
                targets.Add(new NewInventoryItemRecord { ItemUid = uid, CharacterId = characterId, ListType = list, SlotIndex = insert.Slot, Core = core });
                MarkOccupied(occupied, characterId, list, insert.Slot);
            }
            return true;
        }

        private bool TryMigrateStackableToLegacyCharacter(
            SqliteConnection connection, SqliteTransaction transaction,
            Dictionary<(int CharacterId, InventoryListType List), HashSet<short>> occupied,
            List<NewInventoryItemRecord> targets,
            int characterId, InventoryListType list, short preferred, short start, short end, ItemCore source,
            out int requiredFreeSlots, out string error)
        {
            var targetItems = targets.Where(target => target.CharacterId == characterId && target.ListType == list).ToList();
            var slots = GetOccupiedSlots(occupied, characterId, list);
            if (!TryBuildStackMigrationPlan(source, preferred, start, end, targetItems, slots, out var plan, out requiredFreeSlots, out error))
                return false;
            if (plan.IsExactMirror)
                return true;
            ApplyLegacyStackMerges(connection, transaction, plan);
            foreach (var insert in plan.Inserts)
            {
                var core = source.Copy();
                core.Count = insert.Count;
                var uid = InsertLegacyCharacterItem(connection, transaction, characterId, list, insert.Slot, core, null);
                targets.Add(new NewInventoryItemRecord { ItemUid = uid, CharacterId = characterId, ListType = list, SlotIndex = insert.Slot, Core = core });
                MarkOccupied(occupied, characterId, list, insert.Slot);
            }
            return true;
        }

        private bool TryMigrateStackableToNewCargo(
            SqliteConnection connection, SqliteTransaction transaction,
            Dictionary<int, HashSet<short>> occupied,
            List<NewInventoryItemRecord> targets,
            int accountId, int characterId, short preferred, short end, ItemCore source,
            out int requiredFreeSlots, out string error)
        {
            if (end < 0)
            {
                requiredFreeSlots = 1;
                error = "账号仓库未开通";
                return false;
            }
            var targetItems = targets.Where(target => target.AccountId == accountId).ToList();
            var slots = GetOccupiedCargoSlots(occupied, accountId);
            if (!TryBuildStackMigrationPlan(source, preferred, 0, end, targetItems, slots, out var plan, out requiredFreeSlots, out error))
                return false;
            if (plan.IsExactMirror)
                return true;
            ApplyNewStackMerges(connection, transaction, plan, accountCargo: true);
            foreach (var insert in plan.Inserts)
            {
                var core = source.Copy();
                core.Count = insert.Count;
                var uid = InsertNewAccountCargoItem(connection, transaction, accountId, characterId, insert.Slot, core);
                targets.Add(new NewInventoryItemRecord { ItemUid = uid, AccountId = accountId, CharacterId = characterId, ListType = InventoryListType.AccountCargo, SlotIndex = insert.Slot, Core = core });
                MarkAccountCargoOccupied(occupied, accountId, insert.Slot);
            }
            return true;
        }

        private bool TryMigrateStackableToLegacyCargo(
            SqliteConnection connection, SqliteTransaction transaction,
            Dictionary<int, HashSet<short>> occupied,
            List<NewInventoryItemRecord> targets,
            int accountId, int characterId, short preferred, short end, ItemCore source,
            out int requiredFreeSlots, out string error)
        {
            if (end < 0)
            {
                requiredFreeSlots = 1;
                error = "账号仓库未开通";
                return false;
            }
            var targetItems = targets.Where(target => target.AccountId == accountId).ToList();
            var slots = GetOccupiedCargoSlots(occupied, accountId);
            if (!TryBuildStackMigrationPlan(source, preferred, 0, end, targetItems, slots, out var plan, out requiredFreeSlots, out error))
                return false;
            if (plan.IsExactMirror)
                return true;
            ApplyLegacyStackMerges(connection, transaction, plan, accountCargo: true);
            foreach (var insert in plan.Inserts)
            {
                var core = source.Copy();
                core.Count = insert.Count;
                var uid = InsertLegacyAccountCargoItem(connection, transaction, accountId, characterId, insert.Slot, core);
                targets.Add(new NewInventoryItemRecord { ItemUid = uid, AccountId = accountId, CharacterId = characterId, ListType = InventoryListType.AccountCargo, SlotIndex = insert.Slot, Core = core });
                MarkAccountCargoOccupied(occupied, accountId, insert.Slot);
            }
            return true;
        }

        private static void ApplyNewStackMerges(SqliteConnection connection, SqliteTransaction transaction, StackMigrationPlan plan, bool accountCargo = false)
        {
            foreach (var merge in plan.Merges)
            {
                merge.Target.Core.Count = checked(merge.Target.Core.Count + merge.AddCount);
                Exec(connection, transaction,
                    $"UPDATE {(accountCargo ? "account_cargo_new_items" : "character_new_items")} SET item_core=@core,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@id;",
                    ("@core", merge.Target.Core.ToBytes()), ("@id", merge.Target.ItemUid));
            }
        }

        private static void ApplyLegacyStackMerges(SqliteConnection connection, SqliteTransaction transaction, StackMigrationPlan plan, bool accountCargo = false)
        {
            foreach (var merge in plan.Merges)
            {
                merge.Target.Core.Count = checked(merge.Target.Core.Count + merge.AddCount);
                Exec(connection, transaction,
                    $"UPDATE {(accountCargo ? "account_cargo_items" : "character_items")} SET stack_count=@count,instance_value=@count,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@id;",
                    ("@count", merge.Target.Core.Count), ("@id", merge.Target.ItemUid));
            }
        }

        private static bool AreEquivalentStackMirrors(ItemCore source, ItemCore target)
            => target != null
                && source.ItemId == target.ItemId
                && InventoryStackRuleService.IsStackable(target)
                && source.Count == target.Count
                && source.ExpireTime == target.ExpireTime;

        private static IEnumerable<short> EnumerateSlots(short preferred, short start, short end)
        {
            var first = Math.Max((int)preferred, start);
            for (var slot = first; slot <= end; slot++) yield return (short)slot;
            for (var slot = (int)start; slot < first; slot++) yield return (short)slot;
        }

        private static HashSet<short> GetOccupiedSlots(
            Dictionary<(int CharacterId, InventoryListType List), HashSet<short>> occupied,
            int characterId,
            InventoryListType list)
        {
            if (!occupied.TryGetValue((characterId, list), out var slots))
                occupied[(characterId, list)] = slots = new HashSet<short>();
            return slots;
        }

        private static HashSet<short> GetOccupiedCargoSlots(Dictionary<int, HashSet<short>> occupied, int accountId)
        {
            if (!occupied.TryGetValue(accountId, out var slots))
                occupied[accountId] = slots = new HashSet<short>();
            return slots;
        }

        private static int? ResolveServerStackLimit(ItemCore source)
            => InventoryStackRuleService.TryGetStackLimit(source, out var limit) ? limit : null;

        private static long InsertNewCharacterItem(SqliteConnection c,SqliteTransaction t,int cid,InventoryListType list,short slot,ItemCore core)
        {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',@cid,@cid,@list,@slot,@core); SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("@cid",cid);cmd.Parameters.AddWithValue("@list",(int)list);cmd.Parameters.AddWithValue("@slot",slot);cmd.Parameters.AddWithValue("@core",core.ToBytes());return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);}

        private static long InsertNewAccountCargoItem(SqliteConnection c,SqliteTransaction t,int aid,int cid,short slot,ItemCore core)
        {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="INSERT INTO account_cargo_new_items(account_id,character_id,list_type,slot_index,item_core) VALUES(@aid,@cid,12,@slot,@core); SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("@aid",aid);cmd.Parameters.AddWithValue("@cid",cid==0?DBNull.Value:cid);cmd.Parameters.AddWithValue("@slot",slot);cmd.Parameters.AddWithValue("@core",core.ToBytes());return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);}

        private static long InsertLegacyCharacterItem(SqliteConnection c,SqliteTransaction t,int cid,InventoryListType list,short slot,ItemCore core,AvatarDetail avatar)
        {
            using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=@"INSERT INTO character_items(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value,durability,seal_flag,option_value,expire_time,marker_16,pet_serial_or_handle,equipment_lock_id,extra_json)
VALUES('character',@cid,@cid,@list,@slot,@item,@kind,@count,@value,@dur,@seal,@option,@expire,@marker,@pet,@lock,@extra); SELECT last_insert_rowid();";
            BindLegacyCore(cmd,cid,list,slot,core,avatar);return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
        }

        private static long InsertLegacyAccountCargoItem(SqliteConnection c,SqliteTransaction t,int aid,int cid,short slot,ItemCore core)
        {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=@"INSERT INTO account_cargo_items(account_id,slot_index,item_template_id,item_kind,stack_count,instance_value,durability,seal_flag,option_value,expire_time,marker_16,extra_json)
VALUES(@aid,@slot,@item,@kind,@count,@value,@dur,@seal,@option,@expire,@marker,@extra); SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("@aid",aid);BindLegacyCore(cmd,cid,InventoryListType.AccountCargo,slot,core,null,includeIdentity:false);return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);}

        private static void BindLegacyCore(SqliteCommand cmd,int cid,InventoryListType list,short slot,ItemCore core,AvatarDetail avatar,bool includeIdentity=true)
        {if(includeIdentity){cmd.Parameters.AddWithValue("@cid",cid);cmd.Parameters.AddWithValue("@list",(int)list);}cmd.Parameters.AddWithValue("@slot",slot);cmd.Parameters.AddWithValue("@item",core.ItemId);cmd.Parameters.AddWithValue("@kind",NewInventoryStore.GetLegacyKindLabel(core.ItemKind));cmd.Parameters.AddWithValue("@count",NewInventoryStore.IsStackableKind(core.ItemKind)?core.Count:core.Value);cmd.Parameters.AddWithValue("@value",core.Value);cmd.Parameters.AddWithValue("@dur",core.Durability);cmd.Parameters.AddWithValue("@seal",core.SealFlag);cmd.Parameters.AddWithValue("@option",core.ItemKind==ItemCore.KindAvatar?core.AbilityNo:0);cmd.Parameters.AddWithValue("@expire",avatar?.ExpireDate??core.ExpireTime);cmd.Parameters.AddWithValue("@marker",core.Marker16);cmd.Parameters.AddWithValue("@pet",core.ItemKind==ItemCore.KindCreature||core.ItemKind==ItemCore.KindCreatureEquipment||core.ItemKind==ItemCore.KindCreatureConsumable?core.Value:0);cmd.Parameters.AddWithValue("@lock",core.EquipmentLockId);cmd.Parameters.AddWithValue("@extra",BuildLegacyExtraJson(core,avatar));}

        private static string BuildLegacyExtraJson(ItemCore core,AvatarDetail avatar)
        {
            var prefix=new byte[8];BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0,4),core.EnchantCardId);prefix[4]=core.EnchantUpgradeCount;prefix[5]=core.AmplifyType;BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(6,2),core.AmplifyValue);
            var middle=new byte[17];var chron=core.ChronicleOptions;middle[0]=(byte)Math.Min(2,chron.Count);for(var i=0;i<middle[0];i++){var o=chron[i];var off=1+i*8;BinaryPrimitives.WriteInt32LittleEndian(middle.AsSpan(off,4),o.OptionId);middle[off+4]=o.CharacJob;middle[off+5]=o.FirstGrowType;middle[off+6]=o.EquipmentType;middle[off+7]=o.OptionNo;}
            var tail=new byte[37];tail[0]=core.EmblemSocketCount;BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(1,4),core.EmblemId1);BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(5,4),core.EmblemId2);BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(9,2),core.Rune);tail[12]=core.RandomOption0.Type;tail[13]=core.RandomOption1.Type;tail[14]=core.RandomOption2.Type;tail[15]=core.RandomOption0.Value1;tail[16]=core.RandomOption1.Value1;tail[17]=core.RandomOption2.Value1;tail[18]=core.RandomOption0.Value2;tail[19]=core.RandomOption1.Value2;tail[20]=core.RandomOption2.Value2;tail[21]=core.RandomOptionState;tail[22]=core.RandomOptionChangedIndex;tail[23]=core.RandomOptionChangeState;tail[24]=core.RandomOptionChange.Type;tail[25]=core.RandomOptionChange.Value1;tail[26]=core.RandomOptionChange.Value2;tail[27]=core.GenuineUpgrade;tail[28]=core.EmancipateEquipmentLevel;tail[29]=core.TradeRestriction;BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(30,2),core.TailUnknown0);tail[32]=core.TailUnknown1;tail[33]=core.TailUnknown2;tail[34]=core.TailUnknown3;tail[35]=core.RemainUseCount;tail[36]=core.SortLockFlag;
            var json=new JsonObject{{"extData0",core.Attr},{"prefixData0E",Convert.ToHexString(prefix)},{"middleData1A",Convert.ToHexString(middle)},{"tailData2F",Convert.ToHexString(tail)}};
            if(core.ItemKind==ItemCore.KindAvatar){var r0=new byte[5];r0[4]=core.Attr;var r1=new byte[71];r1[0]=(byte)(core.AbilityNo>>8);r1[1]=core.SealFlag;Buffer.BlockCopy(prefix,0,r1,2,8);BinaryPrimitives.WriteInt32LittleEndian(r1.AsSpan(10,4),core.Marker16);Buffer.BlockCopy(middle,0,r1,14,17);BinaryPrimitives.WriteInt32LittleEndian(r1.AsSpan(31,4),core.ExpireTime);Buffer.BlockCopy(tail,0,r1,35,36);var colors=new byte[7];if(avatar!=null){BinaryPrimitives.WriteUInt16LittleEndian(colors.AsSpan(0,2),avatar.Color1);BinaryPrimitives.WriteUInt16LittleEndian(colors.AsSpan(2,2),avatar.Color2);}json["reserved0"]=Convert.ToHexString(r0);json["reserved1"]=Convert.ToHexString(r1);json["reserved2"]=Convert.ToHexString(avatar?.JewelSocket??new byte[30]);json["tailData"]=Convert.ToHexString(colors);}
            if(core.ItemKind==ItemCore.KindCreature||core.ItemKind==ItemCore.KindCreatureEquipment||core.ItemKind==ItemCore.KindCreatureConsumable){var pet=new byte[74];pet[0]=core.Attr;BinaryPrimitives.WriteUInt16LittleEndian(pet.AsSpan(1,2),core.Durability);pet[3]=core.SealFlag;Buffer.BlockCopy(prefix,0,pet,4,8);BinaryPrimitives.WriteInt32LittleEndian(pet.AsSpan(12,4),core.Marker16);Buffer.BlockCopy(middle,0,pet,16,17);BinaryPrimitives.WriteInt32LittleEndian(pet.AsSpan(33,4),core.ExpireTime);Buffer.BlockCopy(tail,0,pet,37,37);json["tailData0A"]=Convert.ToHexString(pet);}
            return json.ToJsonString();
        }

        private static void DeleteLegacyCharacterItem(SqliteConnection c,SqliteTransaction t,long uid)=>Exec(c,t,"DELETE FROM character_items WHERE item_uid=@id;",("@id",uid));
        private static void DeleteLegacyAccountCargoItem(SqliteConnection c,SqliteTransaction t,long uid)=>Exec(c,t,"DELETE FROM account_cargo_items WHERE item_uid=@id;",("@id",uid));
        private static void DeleteLegacyEquipped(SqliteConnection c,SqliteTransaction t,int cid,short slot)=>Exec(c,t,"DELETE FROM character_equipped_entries WHERE character_id=@cid AND slot=@slot;",("@cid",cid),("@slot",slot));
        private static void DeleteNewAccountCargoItem(SqliteConnection c,SqliteTransaction t,long uid)=>Exec(c,t,"DELETE FROM account_cargo_new_items WHERE item_uid=@id;",("@id",uid));
        private static void DeleteNewCharacterItem(SqliteConnection c,SqliteTransaction t,NewInventoryItemRecord row){Exec(c,t,"DELETE FROM character_new_items WHERE item_uid=@id;",("@id",row.ItemUid));if(row.Core.ItemKind==ItemCore.KindAvatar)Exec(c,t,"DELETE FROM character_avatar_detail WHERE item_uid=@id;",("@id",row.Core.AvatarUid));}
        private static void MoveNewCharacterItem(SqliteConnection c,SqliteTransaction t,long uid,InventoryListType list,short slot)=>Exec(c,t,"UPDATE character_new_items SET list_type=@list,slot_index=@slot,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@id;",("@list",(int)list),("@slot",slot),("@id",uid));

        private static bool TryMergeNewVirtualSlot(SqliteConnection c,SqliteTransaction t,int cid,short slot,ItemCore source)
        {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="SELECT item_core FROM character_new_items WHERE owner_scope='character' AND owner_id=@cid AND list_type=0 AND slot_index=@slot;";cmd.Parameters.AddWithValue("@cid",cid);cmd.Parameters.AddWithValue("@slot",slot);if(cmd.ExecuteScalar() is byte[])return true;var core=new ItemCore{ItemKind=ItemCore.KindSpecialMaterial,ItemId=slot,Count=source.Count};Exec(c,t,@"INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',@cid,@cid,0,@slot,@core);",("@cid",cid),("@slot",slot),("@core",core.ToBytes()));return true;}

        private static bool TryMergeLegacyVirtualSlot(SqliteConnection c,SqliteTransaction t,int cid,short slot,ItemCore source)
        {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="SELECT item_uid FROM character_items WHERE owner_scope='character' AND owner_id=@cid AND list_type=0 AND slot_index=@slot LIMIT 1;";cmd.Parameters.AddWithValue("@cid",cid);cmd.Parameters.AddWithValue("@slot",slot);using var r=cmd.ExecuteReader();if(r.Read()){r.Close();return true;}r.Close();InsertLegacyCharacterItem(c,t,cid,InventoryListType.Main,slot,source,null);return true;}

        private static HashSet<long> MigrateLegacyCubesToNew(SqliteConnection c,SqliteTransaction t,InventoryMigrationReport report,HashSet<int> touched,IReadOnlyList<LegacyItemCoreConverter.CharacterItemRow> rows)
        {
            var consumed=new HashSet<long>();
            var groups=new Dictionary<(int AccountId,int ItemId),List<LegacyItemCoreConverter.CharacterItemRow>>();
            foreach(var row in rows)
            {
                if(!TryGetLegacyCubeItemId(row,out var itemId))continue;
                touched.Add(row.CharacterId);
                var accountId=LoadAccountId(c,t,row.CharacterId);
                if(accountId<=0){AddResidual(c,t,report,row.CharacterId,InventoryListType.Main,row.SlotIndex,1,"晶块所属账号不存在");continue;}
                if(!groups.TryGetValue((accountId,itemId),out var sourceRows))groups[(accountId,itemId)]=sourceRows=new List<LegacyItemCoreConverter.CharacterItemRow>();
                sourceRows.Add(row);
            }
            foreach(var group in groups)
            {
                var current=DfoGmTool.ServerCore.Game.Currency.CurrencyService.LoadCubeFragments(c,t,group.Key.AccountId).First(x=>x.ItemId==group.Key.ItemId).Count;
                if(current==0)
                {
                    var sourceCount=group.Value.Max(x=>Math.Max(x.StackCount,x.InstanceValue));
                    DfoGmTool.ServerCore.Game.Currency.CurrencyService.AddCubeFragment(c,t,group.Key.AccountId,group.Key.ItemId,sourceCount);
                }
                foreach(var row in group.Value){DeleteLegacyCharacterItem(c,t,row.ItemUid);consumed.Add(row.ItemUid);report.MigratedItems++;}
            }
            return consumed;
        }

        private static bool TryGetLegacyCubeItemId(LegacyItemCoreConverter.CharacterItemRow row,out int itemId)
        {itemId=0;if(row.ListType!=InventoryListType.Main||row.SlotIndex<354||row.SlotIndex>359)return false;itemId=row.ItemTemplateId>0?row.ItemTemplateId:row.SlotIndex switch{354=>3033,355=>3034,356=>3035,357=>3036,358=>3037,359=>3262,_=>0};return DfoGmTool.ServerCore.Game.Currency.CurrencyService.IsCubeFragment(itemId);}

        private static bool HasMatchingNewAccountCargo(SqliteConnection c,SqliteTransaction t,int accountId,short slot,int itemId)
        {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="SELECT item_core FROM account_cargo_new_items WHERE account_id=@aid AND slot_index=@slot LIMIT 1;";cmd.Parameters.AddWithValue("@aid",accountId);cmd.Parameters.AddWithValue("@slot",slot);return cmd.ExecuteScalar() is byte[] bytes&&ItemCore.FromBytes(bytes).ItemId==itemId;}

        private static bool HasMatchingLegacyAccountCargo(SqliteConnection c,SqliteTransaction t,int accountId,short slot,int itemId)
        {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="SELECT COUNT(*) FROM account_cargo_items WHERE account_id=@aid AND slot_index=@slot AND item_template_id=@item;";cmd.Parameters.AddWithValue("@aid",accountId);cmd.Parameters.AddWithValue("@slot",slot);cmd.Parameters.AddWithValue("@item",itemId);return Convert.ToInt32(cmd.ExecuteScalar(),CultureInfo.InvariantCulture)>0;}

        private static bool TryMigrateLegacyNameTag(SqliteConnection c,SqliteTransaction t,LegacyItemCoreConverter.EquippedEntryRow row)
        {using var check=c.CreateCommand();check.Transaction=t;check.CommandText="SELECT COUNT(*) FROM character_name_tag_state WHERE character_id=@cid AND item_id<>0;";check.Parameters.AddWithValue("@cid",row.CharacterId);if(Convert.ToInt32(check.ExecuteScalar())>0)return false;Exec(c,t,@"INSERT INTO character_name_tag_state(character_id,item_id,expire_time,updated_at) VALUES(@cid,@item,@expire,CURRENT_TIMESTAMP) ON CONFLICT(character_id) DO UPDATE SET item_id=excluded.item_id,expire_time=excluded.expire_time,updated_at=CURRENT_TIMESTAMP;",("@cid",row.CharacterId),("@item",row.ItemTemplateId),("@expire",row.ExpireTime));return true;}

        private static void MigrateNewNameTagsToLegacy(SqliteConnection c,SqliteTransaction t,InventoryMigrationReport report,HashSet<int> touched)
        {var rows=new List<(int Cid,int Item,int Expire)>();using(var cmd=c.CreateCommand()){cmd.Transaction=t;cmd.CommandText="SELECT character_id,item_id,expire_time FROM character_name_tag_state WHERE item_id<>0;";using var r=cmd.ExecuteReader();while(r.Read())rows.Add((r.GetInt32(0),r.GetInt32(1),r.GetInt32(2)));}foreach(var row in rows){touched.Add(row.Cid);using var check=c.CreateCommand();check.Transaction=t;check.CommandText="SELECT COUNT(*) FROM character_equipped_entries WHERE character_id=@cid AND slot=28;";check.Parameters.AddWithValue("@cid",row.Cid);if(Convert.ToInt32(check.ExecuteScalar())>0){Exec(c,t,"DELETE FROM character_name_tag_state WHERE character_id=@cid;",("@cid",row.Cid));report.MigratedItems++;continue;}var core=ItemCore.Create(ItemCore.KindEquipment,row.Item);core.InstanceValue=checked((int)ItemQuality.TopQualitySeed);core.ExpireTime=row.Expire;var raw=MakeEquipListCodec.BuildEntryFromDisplayFields(28,row.Item,ToDisplayFields(core));Exec(c,t,"INSERT INTO character_equipped_entries(character_id,slot,item_id,expire_time,equipment_lock_id,raw_entry) VALUES(@cid,28,@item,@expire,0,@raw);",("@cid",row.Cid),("@item",row.Item),("@expire",row.Expire),("@raw",raw));Exec(c,t,"DELETE FROM character_name_tag_state WHERE character_id=@cid;",("@cid",row.Cid));report.MigratedItems++;}}

        private static MakeEquipListCodec.DisplayFields ToDisplayFields(ItemCore c)
                {
                    var chronicle = c.ChronicleOptions.Select(x => new MakeEquipListCodec.ChronicleOptionFields
                    {
                        OptionId = x.OptionId,
                        CharacJob = x.CharacJob,
                        FirstGrowType = x.FirstGrowType,
                        EquipmentType = x.EquipmentType,
                        OptionNo = x.OptionNo,
                    }).ToArray();
                    return new MakeEquipListCodec.DisplayFields
                    {
                        InstanceValue = unchecked((uint)c.Value),
                        Reinforce = c.Attr,
                        Durability = c.Durability,
                        SealFlag = c.SealFlag,
                        Enchant = unchecked((uint)c.EnchantCardId),
                        EnchantUpgradeCount = c.EnchantUpgradeCount,
                        AmplifyType = c.AmplifyType,
                        AmplifyValue = c.AmplifyValue,
                        Marker16 = unchecked((uint)c.Marker16),
                        ChronicleOptions = chronicle,
                        ExpireTime = c.ExpireTime,
                        EmblemSocketCount = c.EmblemSocketCount,
                        EmblemId1 = c.EmblemId1,
                        EmblemId2 = c.EmblemId2,
                        Rune = c.Rune,
                        MagicSealCount = c.RandomOptionCount,
                        MagicSealTypes = new[] { c.RandomOption0.Type, c.RandomOption1.Type, c.RandomOption2.Type },
                        MagicSealVal1s = new[] { c.RandomOption0.Value1, c.RandomOption1.Value1, c.RandomOption2.Value1 },
                        MagicSealVal2s = new[] { c.RandomOption0.Value2, c.RandomOption1.Value2, c.RandomOption2.Value2 },
                        RandomOptionState = c.RandomOptionState,
                        RandomOptionChangedIndex = c.RandomOptionChangedIndex,
                        RandomOptionChangeState = c.RandomOptionChangeState,
                        RandomOptionChangeType = c.RandomOptionChange.Type,
                        RandomOptionChangeValue1 = c.RandomOptionChange.Value1,
                        RandomOptionChangeValue2 = c.RandomOptionChange.Value2,
                        Forging = c.GenuineUpgrade,
                        EmancipateEquipmentLevel = c.EmancipateEquipmentLevel,
                        TradeRestriction = c.TradeRestriction,
                        TailUnknown0 = c.TailUnknown0,
                        TailUnknown1 = c.TailUnknown1,
                        TailUnknown2 = c.TailUnknown2,
                        TailUnknown3 = c.TailUnknown3,
                        RemainUseCount = c.RemainUseCount,
                        SortLockFlag = c.SortLockFlag,
                        JewelSocket = Array.Empty<byte>(),
                        ExpansionData = Array.Empty<byte>(),
                    };
                }

        private static void InsertAvatarDetail(SqliteConnection c,SqliteTransaction t,AvatarDetail d)=>Exec(c,t,"INSERT INTO character_avatar_detail(item_uid,owner_id,character_id,item_id,expire_date,clear_avatar_id,jewel_socket,color1,color2,delete_date) VALUES(@uid,@owner,@cid,@item,@expire,@clear,@socket,@c1,@c2,@delete);",("@uid",d.AvatarUid),("@owner",d.OwnerId),("@cid",d.CharacterId),("@item",d.ItemId),("@expire",d.ExpireDate),("@clear",d.ClearAvatarId),("@socket",d.JewelSocket),("@c1",d.Color1),("@c2",d.Color2),("@delete",d.DeleteDate));
        private static long AllocateSequence(SqliteConnection c,SqliteTransaction t,string table){using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=$"INSERT INTO {table} DEFAULT VALUES; SELECT last_insert_rowid();";return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);}
        private static void UpdateLock(SqliteConnection c,SqliteTransaction t,int cid,byte lockId,InventoryListType list,short slot){if(lockId==0)return;Exec(c,t,"UPDATE character_item_locks SET inventory_list_type=@list,slot=@slot WHERE character_id=@cid AND equipment_lock_id=@lock;",("@list",(int)list),("@slot",slot),("@cid",cid),("@lock",lockId));}
        private static int LoadAccountId(SqliteConnection c,SqliteTransaction t,int cid){using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="SELECT account_id FROM characters WHERE character_id=@cid;";cmd.Parameters.AddWithValue("@cid",cid);return Convert.ToInt32(cmd.ExecuteScalar()??0,CultureInfo.InvariantCulture);}

        private static void AddResidual(SqliteConnection c,SqliteTransaction t,InventoryMigrationReport report,int cid,InventoryListType list,short slot,int count,string reason,int accountId=0,int requiredFreeSlots=-1)
        {var bag=BagLabel(list,slot);var required=requiredFreeSlots>=0?requiredFreeSlots:count;var resolvedAccountId=accountId>0?accountId:LoadAccountId(c,t,cid);var existing=report.Residuals.FirstOrDefault(x=>x.CharacterId==cid&&x.AccountId==resolvedAccountId&&x.BagType==bag&&x.Reason==reason);if(existing!=null){existing.ItemCount+=count;existing.RequiredFreeSlots+=required;return;}report.Residuals.Add(new InventoryMigrationResidual{CharacterId=cid,CharacterName=LoadCharacterName(c,t,cid),AccountId=resolvedAccountId,BagType=bag,ItemCount=count,RequiredFreeSlots=required,Reason=reason});}
        private static string BagLabel(InventoryListType list,short slot)=>list switch{InventoryListType.Main when slot<=8=>"快捷/货币栏",InventoryListType.Main when slot<=64=>"装备背包",InventoryListType.Main when slot<=120=>"消耗品背包",InventoryListType.Main when slot<=176=>"材料背包",InventoryListType.Main when slot<=232=>"任务品背包",InventoryListType.Main when slot<=288=>"副职业材料背包",InventoryListType.Main=>"徽章背包",InventoryListType.Avatar=>"时装背包",InventoryListType.PersonalCargo=>"个人仓库",InventoryListType.Equipment=>"穿戴栏",InventoryListType.Pet when slot<=139=>"宠物背包",InventoryListType.Pet when slot<=188=>"宠物装备背包",InventoryListType.Pet=>"宠物用品背包",InventoryListType.AccountCargo=>"账号仓库",InventoryListType.TitleBookGeneral=>"普通称号簿",InventoryListType.TitleBookSpecific=>"特殊称号簿",InventoryListType.TitleBookPvp=>"决斗场称号簿",InventoryListType.TitleBookDespair=>"绝望之塔称号簿",InventoryListType.TitleBookEvent=>"活动称号簿",_=>list.ToString()};
        private static string LoadCharacterName(SqliteConnection c,SqliteTransaction t,int cid){if(cid<=0)return null;using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="SELECT name FROM characters WHERE character_id=@cid;";cmd.Parameters.AddWithValue("@cid",cid);var value=cmd.ExecuteScalar();if(value is byte[] bytes)return Encoding.UTF8.GetString(bytes).TrimEnd('\0');return Convert.ToString(value,CultureInfo.InvariantCulture);}
        private static void PopulateCharacterReport(SqliteConnection c,SqliteTransaction t,InventoryMigrationReport report,HashSet<int> touched){var residual=report.Residuals.Where(x=>x.CharacterId>0).Select(x=>x.CharacterId).ToHashSet();foreach(var cid in touched.OrderBy(x=>x)){if(residual.Contains(cid))continue;report.CompleteCharacters.Add(new{characterId=cid,name=LoadCharacterName(c,t,cid),accountId=LoadAccountId(c,t,cid)});}report.MigratedCharacters=report.CompleteCharacters.Count;}

        private static InventoryMigrationStatus ReadStatus(SqliteConnection c,bool running)=>new InventoryMigrationStatus{Running=running,LegacyItemCount=Scalar(c,"SELECT COUNT(*) FROM character_items;")+CountLegacyTitleBookItems(c),LegacyEquippedCount=Scalar(c,"SELECT COUNT(*) FROM character_equipped_entries;"),LegacyAccountCargoCount=Scalar(c,"SELECT COUNT(*) FROM account_cargo_items;"),NewItemCount=Scalar(c,"SELECT COUNT(*) FROM character_new_items;")+Scalar(c,"SELECT COUNT(*) FROM character_name_tag_state WHERE item_id<>0;")+Scalar(c,"SELECT COUNT(*) FROM character_new_titlebook;"),NewAccountCargoCount=Scalar(c,"SELECT COUNT(*) FROM account_cargo_new_items;")};
        private static int CountLegacyTitleBookItems(SqliteConnection c)
        {
            var itemCount = LoadLegacyTitleBooks(c, null).Values.Sum(items => items.Count);
            if (itemCount > 0) return itemCount;
            return Scalar(c, "SELECT COUNT(*) FROM character_titlebook;")
                + Scalar(c, "SELECT COUNT(*) FROM character_achievement_chunks;");
        }
        private static int Scalar(SqliteConnection c,string sql){using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt32(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);}
        private SqliteConnection OpenConnection(){var c=new SqliteConnection(_connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA busy_timeout=30000;";cmd.ExecuteNonQuery();return c;}
        private static void Exec(SqliteConnection c,SqliteTransaction t,string sql,params (string Name,object Value)[] values){using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=sql;foreach(var value in values)cmd.Parameters.AddWithValue(value.Name,value.Value??DBNull.Value);cmd.ExecuteNonQuery();}
    }
}
