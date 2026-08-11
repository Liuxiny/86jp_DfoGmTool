using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class NewInventoryItemRecord
    {
        public long ItemUid { get; set; }
        public int CharacterId { get; set; }
        public int AccountId { get; set; }
        public InventoryListType ListType { get; set; }
        public short SlotIndex { get; set; }
        internal ItemCore Core { get; set; }
        internal AvatarDetail AvatarDetail { get; set; }

        public int ItemTemplateId => Core.ItemId;
        public string ItemKind => NewInventoryStore.GetLegacyKindLabel(Core.ItemKind);
        public int Count => NewInventoryStore.IsStackableKind(Core.ItemKind) ? Core.Count : 1;
        public int InstanceValue => Core.Value;
        public int ExpireTime => AvatarDetail != null ? AvatarDetail.ExpireDate : Core.ExpireTime;
    }

    public sealed class NewInventoryWalletSnapshot
    {
        public int Gold { get; set; }
        public int ReviveCoin { get; set; }
        public int Sp { get; set; }
    }

    /// <summary>
    /// GM 离线仓储。正常业务只读写新版 ItemCore 表；旧表仅由显式迁移服务访问。
    /// 每个写操作均在一个 SQLite 事务内同时维护 core、detail、锁与 v2 审计。
    /// </summary>
    public sealed partial class NewInventoryStore
    {
        private readonly string _connectionString;

        public NewInventoryStore(string databasePath, string schemaFilePath)
        {
            _connectionString = SqliteDatabaseBootstrap.Initialize(databasePath, schemaFilePath);
        }

        public List<NewInventoryItemRecord> LoadCharacterItems(int characterId, int accountId)
        {
            using var connection = OpenConnection();
            var avatarDetails = LoadAvatarDetails(connection, null, characterId);
            var result = new List<NewInventoryItemRecord>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT item_uid, character_id, list_type, slot_index, item_core
FROM character_new_items
WHERE owner_scope = 'character' AND owner_id = @cid
ORDER BY list_type, slot_index;";
                command.Parameters.AddWithValue("@cid", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var core = ReadCore(reader, 4);
                    result.Add(new NewInventoryItemRecord
                    {
                        ItemUid = reader.GetInt64(0),
                        CharacterId = reader.IsDBNull(1) ? characterId : reader.GetInt32(1),
                        AccountId = accountId,
                        ListType = (InventoryListType)reader.GetInt32(2),
                        SlotIndex = checked((short)reader.GetInt32(3)),
                        Core = core,
                        AvatarDetail = core.ItemKind == ItemCore.KindAvatar && avatarDetails.TryGetValue(core.AvatarUid, out var detail) ? detail : null,
                    });
                }
            }

            result.AddRange(LoadAccountCargo(connection, null, accountId));
            return result;
        }

        public List<NewInventoryItemRecord> LoadAccountCargo(int accountId)
        {
            using var connection = OpenConnection();
            return LoadAccountCargo(connection, null, accountId);
        }

        private static List<NewInventoryItemRecord> LoadAccountCargo(SqliteConnection connection, SqliteTransaction transaction, int accountId)
        {
            var result = new List<NewInventoryItemRecord>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT item_uid, COALESCE(character_id, 0), list_type, slot_index, item_core
FROM account_cargo_new_items
WHERE account_id = @aid
ORDER BY slot_index;";
            command.Parameters.AddWithValue("@aid", accountId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new NewInventoryItemRecord
                {
                    ItemUid = reader.GetInt64(0),
                    CharacterId = reader.GetInt32(1),
                    AccountId = accountId,
                    ListType = (InventoryListType)reader.GetInt32(2),
                    SlotIndex = checked((short)reader.GetInt32(3)),
                    Core = ReadCore(reader, 4),
                });
            }
            return result;
        }

        public bool TryLoadItem(int characterId, int accountId, InventoryListType listType, short slotIndex, out NewInventoryItemRecord record)
        {
            using var connection = OpenConnection();
            return TryLoadItem(connection, null, characterId, accountId, listType, slotIndex, out record);
        }

        internal static bool TryLoadItem(SqliteConnection connection, SqliteTransaction transaction, int characterId, int accountId, InventoryListType listType, short slotIndex, out NewInventoryItemRecord record)
        {
            record = null;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            if (listType == InventoryListType.AccountCargo)
            {
                command.CommandText = @"
SELECT item_uid, COALESCE(character_id, 0), list_type, slot_index, item_core
FROM account_cargo_new_items WHERE account_id=@owner AND slot_index=@slot LIMIT 1;";
                command.Parameters.AddWithValue("@owner", accountId);
            }
            else
            {
                command.CommandText = @"
SELECT item_uid, COALESCE(character_id, 0), list_type, slot_index, item_core
FROM character_new_items
WHERE owner_scope='character' AND owner_id=@owner AND list_type=@list AND slot_index=@slot LIMIT 1;";
                command.Parameters.AddWithValue("@owner", characterId);
                command.Parameters.AddWithValue("@list", (int)listType);
            }
            command.Parameters.AddWithValue("@slot", slotIndex);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return false;
            var core = ReadCore(reader, 4);
            record = new NewInventoryItemRecord
            {
                ItemUid = reader.GetInt64(0),
                CharacterId = reader.GetInt32(1),
                AccountId = accountId,
                ListType = (InventoryListType)reader.GetInt32(2),
                SlotIndex = checked((short)reader.GetInt32(3)),
                Core = core,
            };
            reader.Close();
            if (core.ItemKind == ItemCore.KindAvatar)
                record.AvatarDetail = LoadAvatarDetail(connection, transaction, core.AvatarUid);
            return true;
        }

        public ItemGrantResult TryGrant(int characterId, int accountId, int job, int itemTemplateId, int requestedCount, ItemGrantOptions options)
        {
            var result = new ItemGrantResult
            {
                ItemTemplateId = itemTemplateId,
                RequestedCount = requestedCount,
                AssignedSlot = -1,
            };
            if (requestedCount <= 0)
                return Fail(result, "数量必须大于 0");

            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            if (metadata == null || string.Equals(metadata.ItemKind, "special", StringComparison.Ordinal))
                return Fail(result, "物品模板不存在");
            if (!TryResolveKindAndRange(metadata, options?.ManualGrantType, out var itemKind, out var listType, out var start, out var end, out var kindError))
                return Fail(result, kindError);

            result.ListType = listType;
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (!TryGetCharacterOpenRange(
                    connection, transaction, characterId, itemKind,
                    out listType, out start, out end, out var rangeError))
                return Fail(result, rangeError);
            result.ListType = listType;

            if (!TryResolveExpiration(itemTemplateId, metadata, options, itemKind == ItemCore.KindAvatar, out var expireTime, out var expireError))
                return Fail(result, expireError);

            byte avatarAbility = 0;
            if (itemKind == ItemCore.KindAvatar
                && !TryValidateAvatarOptions(itemTemplateId, job, options, out avatarAbility, out expireTime, out var avatarError))
                return Fail(result, avatarError);

            var remaining = requestedCount;
            if (IsStackableKind(itemKind))
            {
                var limit = metadata.StackLimit <= 0 ? int.MaxValue : metadata.StackLimit;
                foreach (var existing in LoadList(connection, transaction, characterId, listType, start, end))
                {
                    if (remaining <= 0)
                        break;
                    if (existing.Core.ItemKind != itemKind || existing.Core.ItemId != itemTemplateId || existing.Core.ExpireTime != expireTime)
                        continue;
                    var room = Math.Max(0L, (long)limit - existing.Core.Count);
                    if (room <= 0)
                        continue;
                    var added = (int)Math.Min(room, remaining);
                    var before = existing.Core.Copy();
                    existing.Core.Count += added;
                    UpdateCore(connection, transaction, existing, before, "gm_grant_stack");
                    remaining -= added;
                    AddAffected(result, existing.SlotIndex, added);
                }
            }

            var occupied = LoadOccupiedSlots(connection, transaction, characterId, listType, start, end);
            while (remaining > 0)
            {
                var slot = FirstFree(occupied, start, end);
                if (slot < 0)
                    return Fail(result, "背包空间不足");

                var perSlot = IsStackableKind(itemKind)
                    ? Math.Min(remaining, metadata.StackLimit <= 0 ? remaining : metadata.StackLimit)
                    : 1;
                var core = CreateDefaultCore(metadata, itemKind, itemTemplateId, perSlot, options, expireTime, out var coreError);
                if (core == null)
                    return Fail(result, coreError);

                if (itemKind == ItemCore.KindAvatar)
                {
                    var avatarUid = AllocateSequence(connection, transaction, "character_avatar_uid_sequence", "avatar_uid");
                    core.AvatarUid = checked((int)avatarUid);
                    core.AbilityNo = avatarAbility;
                    InsertAvatarDetail(connection, transaction, avatarUid, accountId, characterId, itemTemplateId, expireTime);
                }
                else if (itemKind == ItemCore.KindCreature)
                {
                    var creatureUid = AllocateSequence(connection, transaction, "character_creature_uid_sequence", "creature_uid");
                    core.CreatureUid = checked((int)creatureUid);
                    InsertCreatureDetail(connection, transaction, characterId, creatureUid);
                }

                var uid = InsertCharacterCore(connection, transaction, characterId, listType, (short)slot, core);
                WriteAudit(connection, transaction, "gm_grant", characterId, accountId, listType, (short)slot, null, core, uid);
                occupied.Add((short)slot);
                remaining -= perSlot;
                AddAffected(result, (short)slot, perSlot);
            }

            transaction.Commit();
            result.Success = true;
            result.GrantedCount = requestedCount;
            result.ExpireTime = expireTime;
            return result;
        }

        public bool TryDelete(int characterId, int accountId, InventoryListType listType, short slotIndex, int count, out int remaining)
        {
            remaining = 0;
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (!TryLoadItem(connection, transaction, characterId, accountId, listType, slotIndex, out var record))
                return false;
            if (listType == InventoryListType.Main && (slotIndex <= 2 || (slotIndex >= 354 && slotIndex <= 359)))
                return false;

            var before = record.Core.Copy();
            if (IsStackableKind(record.Core.ItemKind) && count > 0 && count < record.Core.Count)
            {
                record.Core.Count -= count;
                remaining = record.Core.Count;
                UpdateCore(connection, transaction, record, before, "gm_delete_partial");
            }
            else
            {
                DeleteCoreRow(connection, transaction, record);
                DeleteAssociatedState(connection, transaction, characterId, record.Core);
                WriteAudit(connection, transaction, "gm_delete", characterId, accountId, listType, slotIndex, before, null, record.ItemUid);
            }
            transaction.Commit();
            return true;
        }

        public bool TryRemoveByTemplateId(int characterId, int accountId, int itemTemplateId, int count, out short slot, out int remaining)
        {
            slot = -1;
            remaining = 0;
            if (count <= 0)
                return false;
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var candidates = LoadCharacterRowsByTemplate(connection, transaction, characterId, itemTemplateId);
            var available = candidates.Sum(x => IsStackableKind(x.Core.ItemKind) ? Math.Max(0, x.Core.Count) : 1);
            if (available < count)
                return false;
            var needed = count;
            foreach (var record in candidates)
            {
                if (needed <= 0)
                    break;
                slot = record.SlotIndex;
                var amount = IsStackableKind(record.Core.ItemKind) ? record.Core.Count : 1;
                var before = record.Core.Copy();
                if (IsStackableKind(record.Core.ItemKind) && needed < amount)
                {
                    record.Core.Count -= needed;
                    remaining = record.Core.Count;
                    UpdateCore(connection, transaction, record, before, "gm_remove_template_partial");
                    needed = 0;
                }
                else
                {
                    needed -= amount;
                    DeleteCoreRow(connection, transaction, record);
                    DeleteAssociatedState(connection, transaction, characterId, record.Core);
                    WriteAudit(connection, transaction, "gm_remove_template", characterId, accountId, record.ListType, record.SlotIndex, before, null, record.ItemUid);
                }
            }
            transaction.Commit();
            return true;
        }

        public NewInventoryWalletSnapshot LoadWallet(int characterId)
        {
            using var connection = OpenConnection();
            var wallet = new NewInventoryWalletSnapshot();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT slot_index, item_core FROM character_new_items
WHERE owner_scope='character' AND owner_id=@cid AND list_type=0 AND slot_index IN (0,1,2);";
            command.Parameters.AddWithValue("@cid", characterId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slot = reader.GetInt32(0);
                var value = ReadCore(reader, 1).Count;
                if (slot == 0) wallet.Gold = value;
                else if (slot == 1) wallet.ReviveCoin = value;
                else if (slot == 2) wallet.Sp = value;
            }
            return wallet;
        }

        public bool TrySetVirtualCount(int characterId, int accountId, short slotIndex, int value)
        {
            if (slotIndex < 0 || slotIndex > 2 || value < 0)
                return false;
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            ItemCore before = null;
            if (TryLoadItem(connection, transaction, characterId, accountId, InventoryListType.Main, slotIndex, out var record))
                before = record.Core.Copy();
            var core = new ItemCore { ItemKind = ItemCore.KindSpecialMaterial, ItemId = slotIndex, Count = value };
            UpsertCharacterCore(connection, transaction, characterId, InventoryListType.Main, slotIndex, core);
            WriteAudit(connection, transaction, "gm_virtual_count_set", characterId, accountId, InventoryListType.Main, slotIndex, before, core, 0);
            transaction.Commit();
            return true;
        }

        public bool TryAdjustVirtualCount(int characterId, int accountId, short slotIndex, int delta, int maximum, out int value)
        {
            value = 0;
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            ItemCore before = null;
            if (TryLoadItem(connection, transaction, characterId, accountId, InventoryListType.Main, slotIndex, out var record))
                before = record.Core.Copy();
            var current = before?.Count ?? 0;
            var next = (long)current + delta;
            if (next < 0)
                return false;
            if (maximum >= 0)
                next = Math.Min(next, maximum);
            value = checked((int)Math.Min(next, int.MaxValue));
            var core = new ItemCore { ItemKind = ItemCore.KindSpecialMaterial, ItemId = slotIndex, Count = value };
            UpsertCharacterCore(connection, transaction, characterId, InventoryListType.Main, slotIndex, core);
            WriteAudit(connection, transaction, "gm_virtual_count_adjust", characterId, accountId, InventoryListType.Main, slotIndex, before, core, 0);
            transaction.Commit();
            return true;
        }

        internal bool UpdateItemCore(int characterId, int accountId, InventoryListType listType, short slotIndex, Func<ItemCore, string> mutate, out NewInventoryItemRecord updated, out string error)
        {
            updated = null;
            error = null;
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (!TryLoadItem(connection, transaction, characterId, accountId, listType, slotIndex, out var record))
            {
                error = "目标槽位没有物品";
                return false;
            }
            if (!TryValidateOpenItemSlot(connection, transaction, characterId, accountId, record, out error))
                return false;
            var before = record.Core.Copy();
            error = mutate?.Invoke(record.Core);
            if (!string.IsNullOrEmpty(error))
                return false;
            UpdateCore(connection, transaction, record, before, "gm_configure");
            transaction.Commit();
            updated = record;
            return true;
        }

        public bool UpdateAvatarDetail(int characterId, int accountId, InventoryListType listType, short slotIndex, ushort? abilityNo, int? expireTime, out string error)
        {
            error = null;
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (!TryLoadItem(connection, transaction, characterId, accountId, listType, slotIndex, out var record)
                || record.Core.ItemKind != ItemCore.KindAvatar)
            {
                error = "目标槽位不是时装";
                return false;
            }
            if (!TryValidateOpenItemSlot(connection, transaction, characterId, accountId, record, out error))
                return false;
            var before = record.Core.Copy();
            if (abilityNo.HasValue)
                record.Core.AbilityNo = abilityNo.Value;
            UpdateCore(connection, transaction, record, before, "gm_configure_avatar");
            if (expireTime.HasValue)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE character_avatar_detail SET expire_date=@expire WHERE item_uid=@uid;";
                command.Parameters.AddWithValue("@expire", expireTime.Value);
                command.Parameters.AddWithValue("@uid", record.Core.AvatarUid);
                if (command.ExecuteNonQuery() == 0)
                {
                    error = "时装 detail 不存在";
                    return false;
                }
            }
            transaction.Commit();
            return true;
        }

        public int DeleteAccountCargoAt(int accountId, short slotIndex)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (!TryLoadItem(connection, transaction, 0, accountId, InventoryListType.AccountCargo, slotIndex, out var record))
                return 0;
            DeleteCoreRow(connection, transaction, record);
            WriteAudit(connection, transaction, "gm_account_cargo_delete", record.CharacterId, accountId, InventoryListType.AccountCargo, slotIndex, record.Core, null, record.ItemUid);
            transaction.Commit();
            return 1;
        }

        public int ClearAccountCargo(int accountId)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var rows = LoadAccountCargo(connection, transaction, accountId);
            foreach (var row in rows)
                WriteAudit(connection, transaction, "gm_account_cargo_clear", row.CharacterId, accountId, InventoryListType.AccountCargo, row.SlotIndex, row.Core, null, row.ItemUid);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM account_cargo_new_items WHERE account_id=@aid;";
            command.Parameters.AddWithValue("@aid", accountId);
            var deleted = command.ExecuteNonQuery();
            transaction.Commit();
            return deleted;
        }

        public int SortRange(int characterId, int accountId, InventoryListType listType, short start, short end)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var rows = LoadList(connection, transaction, characterId, listType, start, end)
                .Where(row => row.Core.SortLockFlag == 0)
                .OrderBy(row => row.Core.ItemKind)
                .ThenBy(row => row.Core.ItemId)
                .ThenBy(row => row.Core.ExpireTime)
                .ThenBy(row => row.SlotIndex)
                .ToList();
            var locked = LoadList(connection, transaction, characterId, listType, start, end)
                .Where(row => row.Core.SortLockFlag != 0)
                .Select(row => row.SlotIndex)
                .ToHashSet();
            var targets = Enumerable.Range(start, end - start + 1).Select(x => (short)x).Where(x => !locked.Contains(x)).ToList();
            var changed = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].SlotIndex == targets[i])
                    continue;
                SetSlot(connection, transaction, rows[i], checked((short)(-10000 - i)));
            }
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].SlotIndex == targets[i])
                    continue;
                var old = rows[i].SlotIndex;
                SetSlot(connection, transaction, rows[i], targets[i]);
                WriteAudit(connection, transaction, "gm_sort", characterId, accountId, listType, targets[i], rows[i].Core, rows[i].Core, rows[i].ItemUid);
                rows[i].SlotIndex = targets[i];
                changed++;
            }
            transaction.Commit();
            return changed;
        }

        public void UpsertNameTag(int characterId, int itemId, int expireTime)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO character_name_tag_state(character_id,item_id,expire_time,updated_at)
VALUES(@cid,@item,@expire,CURRENT_TIMESTAMP)
ON CONFLICT(character_id) DO UPDATE SET item_id=excluded.item_id, expire_time=excluded.expire_time, updated_at=CURRENT_TIMESTAMP;";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@item", itemId);
            command.Parameters.AddWithValue("@expire", expireTime);
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        public (int ItemId, int ExpireTime) LoadNameTag(int characterId)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT item_id, expire_time FROM character_name_tag_state WHERE character_id=@cid;";
            command.Parameters.AddWithValue("@cid", characterId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? (reader.GetInt32(0), reader.GetInt32(1)) : (0, 0);
        }

        internal static bool IsStackableKind(byte kind)
        {
            return kind == ItemCore.KindConsumable || kind == ItemCore.KindMaterial || kind == ItemCore.KindQuest
                || kind == ItemCore.KindCreatureConsumable || kind == ItemCore.KindAvatarEmblem
                || kind == ItemCore.KindExpertJobMaterial || kind == ItemCore.KindSpecialMaterial;
        }

        internal static string GetLegacyKindLabel(byte kind)
        {
            return kind == ItemCore.KindAvatar ? "avatar" : kind == ItemCore.KindCreature || kind == ItemCore.KindCreatureEquipment || kind == ItemCore.KindCreatureConsumable ? "pet" : kind == ItemCore.KindEquipment ? "equipment" : "stackable";
        }

        private static ItemCore CreateDefaultCore(ItemMetadata metadata, byte itemKind, int itemId, int count, ItemGrantOptions options, int expireTime, out string error)
        {
            error = null;
            var core = ItemCore.Create(itemKind, itemId);
            core.ExpireTime = itemKind == ItemCore.KindAvatar ? 0 : expireTime;
            if (IsStackableKind(itemKind))
            {
                core.Count = count;
                return core;
            }
            if (itemKind == ItemCore.KindCreature)
                return core;
            if (itemKind == ItemCore.KindAvatar)
                return core;

            core.InstanceValue = itemKind == ItemCore.KindCreatureEquipment && !metadata.SupportsPetEquipmentQuality
                ? 0
                : checked((int)ItemQuality.ResolveSeed(options?.QualityMode ?? ItemQualityMode.Top));
            core.Durability = metadata.Durability;
            core.SealFlag = metadata.IsSealed ? (byte)1 : (byte)0;
            var capability = EquipmentGrantPolicy.Describe(metadata);
            var upgrade = options?.UpgradeLevel ?? 0;
            var amplifyType = options?.AmplifyType ?? 0;
            var forging = options?.ForgingLevel ?? 0;
            if (upgrade < 0 || upgrade > EquipmentGrantPolicy.MaximumUpgradeLevel)
            {
                error = "强化/增幅等级必须在 0-31 之间";
                return null;
            }
            if (amplifyType < 0 || amplifyType > 4 || (amplifyType > 0 && !capability.CanAmplify))
            {
                error = "红字属性类型无效或该装备不支持增幅";
                return null;
            }
            if (upgrade > 0 && amplifyType == 0 && !capability.CanUpgrade)
            {
                error = "该装备不支持强化";
                return null;
            }
            if (forging < 0 || forging > EquipmentGrantPolicy.MaximumForgingLevel || (forging > 0 && !capability.CanForge))
            {
                error = "锻造等级无效或该装备不是武器";
                return null;
            }
            core.Upgrade = (byte)upgrade;
            core.AmplifyType = (byte)amplifyType;
            core.AmplifyValue = amplifyType > 0 ? AmplifyInitialValueResolver.Resolve(metadata.Rarity) : (ushort)0;
            if (amplifyType > 0 && core.AmplifyValue == 0)
            {
                error = "无法从 PVF 计算红字初始值";
                return null;
            }
            core.GenuineUpgrade = (byte)forging;
            return core;
        }

        private static bool TryValidateAvatarOptions(int itemId, int job, ItemGrantOptions options, out byte ability, out int expireTime, out string error)
        {
            ability = 0;
            expireTime = 0;
            error = null;
            if (!ItemMetadataResolver.TryLoadEquipmentFile(itemId, out var equipment))
            {
                error = "装扮模板无法从 PVF 读取";
                return false;
            }
            if (!AvatarGrantPolicy.IsUsableByJob(equipment.UsableJob, job))
            {
                error = "该装扮不适用于当前角色职业";
                return false;
            }
            var avatarMetadata = AvatarEquipmentMetadataReader.Read(equipment);
            var legal = AvatarGrantPolicy.ResolveOptions(
                equipment.EquipmentType,
                equipment.Grade,
                avatarMetadata.SelectAbilities,
                job,
                avatarMetadata.AbilityCaseIndex);
            var requested = options?.AvatarOptionValue ?? 0;
            if (requested < 0 || requested > byte.MaxValue || !AvatarGrantPolicy.ContainsValue(legal, requested))
            {
                error = "装扮属性不属于当前模板、品级和职业的合法选项";
                return false;
            }
            ability = (byte)requested;
            if (options?.ExpirationDays is int days)
            {
                var durations = AvatarDurationResolver.Resolve(itemId);
                if (!AvatarDurationResolver.ContainsDuration(durations, days))
                {
                    error = "装扮期限不属于该模板的 PVF 支持档位";
                    return false;
                }
                if (days > 0)
                {
                    var value = DateTimeOffset.Now.ToUnixTimeSeconds() + days * 86400L;
                    if (value > int.MaxValue)
                    {
                        error = "装扮期限超出服务端可存储范围";
                        return false;
                    }
                    expireTime = (int)value;
                }
            }
            return true;
        }

        private static bool TryResolveExpiration(int itemId, ItemMetadata metadata, ItemGrantOptions options, bool avatar, out int expireTime, out string error)
        {
            expireTime = 0;
            error = null;
            if (avatar)
                return true;
            if (!ItemGrantExpirationResolver.TryResolve(itemId, metadata, out expireTime, out error))
                return false;
            if (options?.ExpirationDays is int days)
            {
                var capability = new ItemGrantExpirationCapability { IsLimited = expireTime > 0, CanOverride = expireTime > 0, DefaultExpireTime = expireTime };
                if (metadata.IsStackable && StackableExpirationPolicyResolver.TryResolve(metadata.StackableFile, out var policy))
                {
                    capability.IsLimited = policy.RequiresInstanceExpiration || policy.AbsoluteExpirationUnixTime > 0 || policy.DailyDeleteItem;
                    capability.CanOverride = policy.RequiresInstanceExpiration || policy.AbsoluteExpirationUnixTime > 0;
                }
                if (!ItemGrantExpirationOverride.TryResolve(capability, days, DateTimeOffset.Now.ToUnixTimeSeconds(), out expireTime, out error))
                    return false;
            }
            return true;
        }

        private static bool TryResolveKindAndRange(ItemMetadata metadata, string manual, out byte kind, out InventoryListType list, out short start, out short end, out string error)
        {
            kind = ItemCore.KindUnknown;
            list = InventoryListType.Main;
            start = end = 0;
            error = null;
            var token = (manual ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(token))
            {
                switch (token)
                {
                    case "equipment": kind = ItemCore.KindEquipment; break;
                    case "avatar": kind = ItemCore.KindAvatar; break;
                    case "pet": kind = ItemCore.KindCreature; break;
                    case "pet-equipment": kind = ItemCore.KindCreatureEquipment; break;
                    case "consumable": kind = ItemCore.KindConsumable; break;
                    case "material": kind = ItemCore.KindMaterial; break;
                    case "quest": kind = ItemCore.KindQuest; break;
                    case "expert-material": kind = ItemCore.KindExpertJobMaterial; break;
                    case "avatar-emblem": kind = ItemCore.KindAvatarEmblem; break;
                    case "special-material": kind = ItemCore.KindSpecialMaterial; break;
                    case "pet-consumable": kind = ItemCore.KindCreatureConsumable; break;
                    default: error = "手动物品类型无效"; return false;
                }
            }
            else if (string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal))
            {
                if (ItemMetadataResolver.IsAvatarMetadata(metadata)) kind = ItemCore.KindAvatar;
                else if (ItemMetadataResolver.IsPetCreatureMetadata(metadata)) kind = ItemCore.KindCreature;
                else if (ItemMetadataResolver.IsPetArtifactMetadata(metadata)) kind = ItemCore.KindCreatureEquipment;
                else kind = ItemCore.KindEquipment;
            }
            else if (metadata.IsStackable)
            {
                if (ItemMetadataResolver.IsPetConsumableItem(metadata)) kind = ItemCore.KindCreatureConsumable;
                else
                {
                    var tag = ItemMetadataResolver.ResolvePvfTypeTag(metadata);
                    if (tag == "avatar emblem") kind = ItemCore.KindAvatarEmblem;
                    else if (tag == "material expert job") kind = ItemCore.KindExpertJobMaterial;
                    else if (tag == "quest") kind = ItemCore.KindQuest;
                    else if (tag == "material") kind = ItemCore.KindMaterial;
                    else kind = ItemCore.KindConsumable;
                }
            }
            else
            {
                error = "无法解析物品类型";
                return false;
            }

            if (!TryGetRange(kind, out list, out start, out end))
            {
                error = "物品类型没有可用背包范围";
                return false;
            }
            return true;
        }

        internal static bool TryGetRange(byte kind, out InventoryListType list, out short start, out short end)
        {
            list = InventoryListType.Main;
            start = end = 0;
            switch (kind)
            {
                case ItemCore.KindEquipment: start = 9; end = 64; return true;
                case ItemCore.KindConsumable: start = 65; end = 120; return true;
                case ItemCore.KindMaterial: start = 121; end = 176; return true;
                case ItemCore.KindQuest: start = 177; end = 232; return true;
                case ItemCore.KindExpertJobMaterial: start = 233; end = 288; return true;
                case ItemCore.KindAvatarEmblem: start = 289; end = 351; return true;
                case ItemCore.KindAvatar: list = InventoryListType.Avatar; start = 0; end = 209; return true;
                case ItemCore.KindCreature: list = InventoryListType.Pet; start = 0; end = 139; return true;
                case ItemCore.KindCreatureEquipment: list = InventoryListType.Pet; start = 140; end = 188; return true;
                case ItemCore.KindCreatureConsumable: list = InventoryListType.Pet; start = 189; end = 239; return true;
                default: return false;
            }
        }

        internal static bool TryGetCharacterOpenRange(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte kind,
            out InventoryListType list,
            out short start,
            out short end,
            out string error)
        {
            error = null;
            if (!TryGetRange(kind, out list, out start, out end))
            {
                error = "物品类型没有可用背包范围";
                return false;
            }

            if (list != InventoryListType.Main)
                return true;

            var stage = LoadCharacterListParam(connection, transaction, characterId, InventoryListType.Main, 24);
            if (stage != 0 && stage != 8 && stage != 16 && stage != 24)
            {
                error = "角色主背包扩展状态无效: " + stage;
                return false;
            }
            end = checked((short)(end - (24 - stage)));
            return true;
        }

        internal static bool TryFindFirstFreeCharacterBagSlot(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte kind,
            out InventoryListType list,
            out short rangeStart,
            out short destinationSlot,
            out string error)
            => TryFindFirstFreeCharacterBagSlot(
                connection, transaction, characterId, kind, "character_new_items",
                out list, out rangeStart, out destinationSlot, out error);

        internal static bool TryFindFirstFreeLegacyCharacterBagSlot(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte kind,
            out InventoryListType list,
            out short rangeStart,
            out short destinationSlot,
            out string error)
            => TryFindFirstFreeCharacterBagSlot(
                connection, transaction, characterId, kind, "character_items",
                out list, out rangeStart, out destinationSlot, out error);

        private static bool TryFindFirstFreeCharacterBagSlot(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte kind,
            string itemTable,
            out InventoryListType list,
            out short rangeStart,
            out short destinationSlot,
            out string error)
        {
            destinationSlot = -1;
            if (!TryGetCharacterOpenRange(
                    connection, transaction, characterId, kind,
                    out list, out rangeStart, out var rangeEnd, out error))
                return false;

            var occupied = new HashSet<int>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $@"SELECT slot_index FROM {itemTable}
WHERE owner_scope='character'
  AND COALESCE(character_id,owner_id)=@cid
  AND list_type=@list
  AND slot_index BETWEEN @start AND @end;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@list", (int)list);
                command.Parameters.AddWithValue("@start", rangeStart);
                command.Parameters.AddWithValue("@end", rangeEnd);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    occupied.Add(reader.GetInt32(0));
            }

            for (var slot = (int)rangeStart; slot <= rangeEnd; slot++)
            {
                if (occupied.Contains(slot))
                    continue;
                destinationSlot = checked((short)slot);
                error = null;
                return true;
            }

            error = $"{list} 背包已满";
            return false;
        }

        internal static short GetPersonalCargoOpenEnd(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId)
        {
            var capacity = LoadCharacterListParam(connection, transaction, characterId, InventoryListType.PersonalCargo, 8);
            capacity = capacity <= 0 ? 8 : Math.Min(capacity, 152);
            return checked((short)(capacity - 1));
        }

        private static bool TryValidateOpenItemSlot(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int accountId,
            NewInventoryItemRecord record,
            out string error)
        {
            error = null;
            var slot = record.SlotIndex;
            switch (record.ListType)
            {
                case InventoryListType.Main:
                    if (slot >= 0 && slot <= 2)
                        return record.Core.ItemKind == ItemCore.KindSpecialMaterial
                            || FailSlotValidation("货币槽物品类型不匹配", out error);
                    if (slot >= 3 && slot <= 8)
                        return true;
                    if (TryGetCharacterOpenRange(connection, transaction, characterId, record.Core.ItemKind,
                            out var expectedList, out var start, out var end, out error)
                        && expectedList == InventoryListType.Main
                        && slot >= start && slot <= end)
                        return true;
                    return FailSlotValidation(error ?? "物品不在该角色已开放的主背包区间", out error);

                case InventoryListType.Avatar:
                    return record.Core.ItemKind == ItemCore.KindAvatar && slot >= 0 && slot <= 209
                        || FailSlotValidation("时装不在有效时装栏区间", out error);

                case InventoryListType.PersonalCargo:
                    return slot >= 0 && slot <= GetPersonalCargoOpenEnd(connection, transaction, characterId)
                        || FailSlotValidation("物品位于未开放的个人仓库格子", out error);

                case InventoryListType.Pet:
                    if (record.Core.ItemKind == ItemCore.KindCreature) return slot >= 0 && slot <= 139 || FailSlotValidation("宠物槽位不匹配", out error);
                    if (record.Core.ItemKind == ItemCore.KindCreatureEquipment) return slot >= 140 && slot <= 188 || FailSlotValidation("宠物装备槽位不匹配", out error);
                    if (record.Core.ItemKind == ItemCore.KindCreatureConsumable) return slot >= 189 && slot <= 239 || FailSlotValidation("宠物用品槽位不匹配", out error);
                    return FailSlotValidation("宠物背包物品类型无效", out error);

                case InventoryListType.Equipment:
                    if (slot >= 0 && slot <= 10)
                        return record.Core.ItemKind == ItemCore.KindAvatar || FailSlotValidation("穿戴时装槽物品类型不匹配", out error);
                    if (slot >= 11 && slot <= 20 || slot == 29)
                        return record.Core.ItemKind == ItemCore.KindEquipment || FailSlotValidation("穿戴装备槽物品类型不匹配", out error);
                    if (slot >= 21 && slot <= 23)
                    {
                        var flags = LoadExtraEquipmentSlotStat(connection, transaction, characterId);
                        return record.Core.ItemKind == ItemCore.KindEquipment
                            && (flags & (1 << (slot - 21))) != 0
                            || FailSlotValidation("特殊装备槽尚未开放或物品类型不匹配", out error);
                    }
                    if (slot == 24)
                        return record.Core.ItemKind == ItemCore.KindCreature || FailSlotValidation("穿戴宠物槽物品类型不匹配", out error);
                    if (slot >= 25 && slot <= 27)
                        return record.Core.ItemKind == ItemCore.KindCreatureEquipment || FailSlotValidation("穿戴宠物装备槽物品类型不匹配", out error);
                    return FailSlotValidation("穿戴槽位无效", out error);

                case InventoryListType.AccountCargo:
                    var capacity = LoadAccountCargoCapacity(connection, transaction, accountId);
                    return slot >= 0 && slot < capacity
                        || FailSlotValidation("物品位于未开放的账号仓库格子", out error);

                default:
                    return FailSlotValidation("该物品列表不支持配置", out error);
            }
        }

        private static bool FailSlotValidation(string message, out string error)
        {
            error = message;
            return false;
        }

        private static int LoadCharacterListParam(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            InventoryListType listType,
            int defaultValue)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"SELECT list_param16 FROM character_container_state
WHERE character_id=@cid AND list_type=@list LIMIT 1;";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@list", (int)listType);
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? defaultValue
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static int LoadExtraEquipmentSlotStat(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT ex_equip_slot_stat FROM characters WHERE character_id=@cid;";
            command.Parameters.AddWithValue("@cid", characterId);
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? 0
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static int LoadAccountCargoCapacity(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int accountId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT selection_key FROM account_cargo_state WHERE account_id=@aid;";
            command.Parameters.AddWithValue("@aid", accountId);
            var value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                return 0;
            return Math.Max(0, Math.Min(Convert.ToInt32(value, CultureInfo.InvariantCulture), 64));
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static ItemCore ReadCore(SqliteDataReader reader, int ordinal)
        {
            var bytes = (byte[])reader[ordinal];
            if (bytes.Length != ItemCore.Size)
                throw new InvalidOperationException($"item_core 长度必须为 {ItemCore.Size}，实际 {bytes.Length}");
            return ItemCore.FromBytes(bytes);
        }

        private static Dictionary<int, AvatarDetail> LoadAvatarDetails(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            var result = new Dictionary<int, AvatarDetail>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT item_uid, owner_id, character_id, item_id, expire_date, clear_avatar_id, jewel_socket, color1, color2, delete_date
FROM character_avatar_detail WHERE character_id=@cid;";
            command.Parameters.AddWithValue("@cid", characterId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var detail = AvatarDetailCodec.ReadDetail(reader);
                result[checked((int)detail.AvatarUid)] = detail;
            }
            return result;
        }

        private static AvatarDetail LoadAvatarDetail(SqliteConnection connection, SqliteTransaction transaction, int avatarUid)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT item_uid, owner_id, character_id, item_id, expire_date, clear_avatar_id, jewel_socket, color1, color2, delete_date
FROM character_avatar_detail WHERE item_uid=@uid;";
            command.Parameters.AddWithValue("@uid", avatarUid);
            using var reader = command.ExecuteReader();
            return reader.Read() ? AvatarDetailCodec.ReadDetail(reader) : null;
        }

        private static List<NewInventoryItemRecord> LoadList(SqliteConnection connection, SqliteTransaction transaction, int characterId, InventoryListType listType, short start, short end)
        {
            var result = new List<NewInventoryItemRecord>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT item_uid, COALESCE(character_id,0), list_type, slot_index, item_core
FROM character_new_items
WHERE owner_scope='character' AND owner_id=@cid AND list_type=@list AND slot_index BETWEEN @start AND @end
ORDER BY slot_index;";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@list", (int)listType);
            command.Parameters.AddWithValue("@start", start);
            command.Parameters.AddWithValue("@end", end);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(new NewInventoryItemRecord { ItemUid=reader.GetInt64(0), CharacterId=reader.GetInt32(1), ListType=(InventoryListType)reader.GetInt32(2), SlotIndex=checked((short)reader.GetInt32(3)), Core=ReadCore(reader,4) });
            return result;
        }

        private static List<NewInventoryItemRecord> LoadCharacterRowsByTemplate(SqliteConnection connection, SqliteTransaction transaction, int characterId, int itemId)
        {
            var result = new List<NewInventoryItemRecord>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT item_uid, COALESCE(character_id,0), list_type, slot_index, item_core FROM character_new_items WHERE owner_scope='character' AND owner_id=@cid ORDER BY list_type,slot_index;";
            command.Parameters.AddWithValue("@cid", characterId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var core = ReadCore(reader, 4);
                if (core.ItemId == itemId)
                    result.Add(new NewInventoryItemRecord { ItemUid=reader.GetInt64(0), CharacterId=reader.GetInt32(1), ListType=(InventoryListType)reader.GetInt32(2), SlotIndex=checked((short)reader.GetInt32(3)), Core=core });
            }
            return result;
        }

        private static HashSet<short> LoadOccupiedSlots(SqliteConnection connection, SqliteTransaction transaction, int characterId, InventoryListType listType, short start, short end)
            => LoadList(connection, transaction, characterId, listType, start, end).Select(x => x.SlotIndex).ToHashSet();

        private static int FirstFree(HashSet<short> occupied, short start, short end)
        {
            for (var slot = (int)start; slot <= end; slot++)
                if (!occupied.Contains((short)slot)) return slot;
            return -1;
        }

        private static long InsertCharacterCore(SqliteConnection connection, SqliteTransaction transaction, int characterId, InventoryListType listType, short slot, ItemCore core)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core)
VALUES('character',@cid,@cid,@list,@slot,@core);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@list", (int)listType);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core.ToBytes());
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void UpsertCharacterCore(SqliteConnection connection, SqliteTransaction transaction, int characterId, InventoryListType listType, short slot, ItemCore core)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core)
VALUES('character',@cid,@cid,@list,@slot,@core)
ON CONFLICT(owner_scope,owner_id,list_type,slot_index) DO UPDATE SET item_core=excluded.item_core, updated_at=CURRENT_TIMESTAMP;";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@list", (int)listType);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core.ToBytes());
            command.ExecuteNonQuery();
        }

        private static void UpdateCore(SqliteConnection connection, SqliteTransaction transaction, NewInventoryItemRecord record, ItemCore before, string action)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = record.ListType == InventoryListType.AccountCargo
                ? "UPDATE account_cargo_new_items SET item_core=@core,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@uid;"
                : "UPDATE character_new_items SET item_core=@core,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@uid;";
            command.Parameters.AddWithValue("@core", record.Core.ToBytes());
            command.Parameters.AddWithValue("@uid", record.ItemUid);
            command.ExecuteNonQuery();
            WriteAudit(connection, transaction, action, record.CharacterId, record.AccountId, record.ListType, record.SlotIndex, before, record.Core, record.ItemUid);
        }

        private static void DeleteCoreRow(SqliteConnection connection, SqliteTransaction transaction, NewInventoryItemRecord record)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = record.ListType == InventoryListType.AccountCargo
                ? "DELETE FROM account_cargo_new_items WHERE item_uid=@uid;"
                : "DELETE FROM character_new_items WHERE item_uid=@uid;";
            command.Parameters.AddWithValue("@uid", record.ItemUid);
            command.ExecuteNonQuery();
        }

        private static void DeleteAssociatedState(SqliteConnection connection, SqliteTransaction transaction, int characterId, ItemCore core)
        {
            if (core.ItemKind == ItemCore.KindAvatar && core.AvatarUid > 0)
                Execute(connection, transaction, "DELETE FROM character_avatar_detail WHERE item_uid=@value AND NOT EXISTS(SELECT 1 FROM character_new_items WHERE substr(item_core,6,4)=substr(@blob,6,4));", ("@value", core.AvatarUid), ("@blob", core.ToBytes()));
            if (core.ItemKind == ItemCore.KindCreature && core.CreatureUid > 0)
                Execute(connection, transaction, "DELETE FROM character_creatures WHERE character_id=@cid AND creature_key=@value AND NOT EXISTS(SELECT 1 FROM character_new_items WHERE character_id=@cid AND substr(item_core,6,4)=substr(@blob,6,4));", ("@cid", characterId), ("@value", core.CreatureUid), ("@blob", core.ToBytes()));
            if (core.EquipmentLockId != 0)
                Execute(connection, transaction, "DELETE FROM character_item_locks WHERE character_id=@cid AND equipment_lock_id=@lock;", ("@cid", characterId), ("@lock", core.EquipmentLockId));
        }

        private static void SetSlot(SqliteConnection connection, SqliteTransaction transaction, NewInventoryItemRecord record, short slot)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE character_new_items SET slot_index=@slot,updated_at=CURRENT_TIMESTAMP WHERE item_uid=@uid;";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@uid", record.ItemUid);
            command.ExecuteNonQuery();
            if (record.Core.EquipmentLockId != 0)
                Execute(connection, transaction, "UPDATE character_item_locks SET inventory_list_type=@list,slot=@slot WHERE character_id=@cid AND equipment_lock_id=@lock;", ("@list", (int)record.ListType), ("@slot", slot), ("@cid", record.CharacterId), ("@lock", record.Core.EquipmentLockId));
        }

        private static void InsertAvatarDetail(SqliteConnection connection, SqliteTransaction transaction, long uid, int accountId, int characterId, int itemId, int expireTime)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO character_avatar_detail(item_uid,owner_id,character_id,item_id,expire_date,clear_avatar_id,jewel_socket,color1,color2,delete_date)
VALUES(@uid,@aid,@cid,@item,@expire,0,@sockets,0,0,0);";
            command.Parameters.AddWithValue("@uid", uid);
            command.Parameters.AddWithValue("@aid", accountId);
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@item", itemId);
            command.Parameters.AddWithValue("@expire", expireTime);
            command.Parameters.AddWithValue("@sockets", new byte[AvatarSocketDataCodec.Length]);
            command.ExecuteNonQuery();
        }

        private static void InsertCreatureDetail(SqliteConnection connection, SqliteTransaction transaction, int characterId, long creatureUid)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO character_creatures(character_id,sort_order,creature_key,field04,mode_flag,progress_value,mode1_field0a,mode1_field0b,field_after_value,creature_text,tail_flag,extra_json)
VALUES(@cid,COALESCE((SELECT MAX(sort_order)+1 FROM character_creatures WHERE character_id=@cid),0),@uid,100,0,0,0,0,1,NULL,0,'{}');";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@uid", creatureUid);
            command.ExecuteNonQuery();
        }

        private static long AllocateSequence(SqliteConnection connection, SqliteTransaction transaction, string table, string column)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {table} DEFAULT VALUES; SELECT last_insert_rowid();";
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void AddAffected(ItemGrantResult result, short slot, int count)
        {
            if (result.AssignedSlot < 0) result.AssignedSlot = slot;
            if (!result.AffectedSlots.Contains(slot)) result.AffectedSlots.Add(slot);
            result.GrantedCount += count;
        }

        private static ItemGrantResult Fail(ItemGrantResult result, string error)
        {
            result.Success = false;
            result.Error = error;
            return result;
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var pair in parameters) command.Parameters.AddWithValue(pair.Name, pair.Value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        private static void WriteAudit(SqliteConnection connection, SqliteTransaction transaction, string action, int characterId, int accountId, InventoryListType listType, short slot, ItemCore before, ItemCore after, long itemUid)
        {
            static int CountOf(ItemCore core) => core == null ? 0 : IsStackableKind(core.ItemKind) ? Math.Max(0, core.Count) : 1;
            static string HashOf(ItemCore core) => core == null ? null : Convert.ToHexString(SHA256.HashData(core.ToBytes())).ToLowerInvariant();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO inventory_audit_log_v2(session_id,owner_scope,owner_id,character_id,account_id,action_name,list_type,slot_index,item_id,item_kind,value_before,value_after,count_before,count_after,count_delta,before_core_hash,after_core_hash,payload_json)
VALUES('gm-tool',@scope,@owner,@cid,@aid,@action,@list,@slot,@item,@kind,@vb,@va,@cb,@ca,@delta,@hb,@ha,@payload);";
            var ownerScope = listType == InventoryListType.AccountCargo ? "account" : "character";
            command.Parameters.AddWithValue("@scope", ownerScope);
            command.Parameters.AddWithValue("@owner", ownerScope == "account" ? accountId : characterId);
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@aid", accountId);
            command.Parameters.AddWithValue("@action", action);
            command.Parameters.AddWithValue("@list", (int)listType);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@item", after?.ItemId ?? before?.ItemId ?? 0);
            command.Parameters.AddWithValue("@kind", after?.ItemKind ?? before?.ItemKind ?? 0);
            command.Parameters.AddWithValue("@vb", before?.Value ?? 0);
            command.Parameters.AddWithValue("@va", after?.Value ?? 0);
            command.Parameters.AddWithValue("@cb", CountOf(before));
            command.Parameters.AddWithValue("@ca", CountOf(after));
            command.Parameters.AddWithValue("@delta", CountOf(after) - CountOf(before));
            command.Parameters.AddWithValue("@hb", (object)HashOf(before) ?? DBNull.Value);
            command.Parameters.AddWithValue("@ha", (object)HashOf(after) ?? DBNull.Value);
            command.Parameters.AddWithValue("@payload", "{\"source\":\"gm_tool\",\"itemUid\":" + itemUid.ToString(CultureInfo.InvariantCulture) + "}");
            command.ExecuteNonQuery();
        }
    }
}
