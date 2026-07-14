using DfoGmTool.ServerCore.Game.CharacterData;
using DfoGmTool.ServerCore.Game.Inventory;
using Microsoft.Data.Sqlite;
using System;
using System.Linq;

namespace DfoGmTool.ServerCore.Game.TitleBook
{
    public sealed class TitleBookMutationService
    {
        private readonly string _connectionString;
        private readonly InventoryDbPrimitives _db = new InventoryDbPrimitives();
        private readonly CharacterTitleBookRepository _titleBookRepository;
        private readonly CharacterAchievementRepository _achievementRepository;
        private readonly TitleBookStaticDataProvider _staticData;
        private const int TitleBookLockListTypeBase = 19;

        public TitleBookMutationService(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _titleBookRepository = new CharacterTitleBookRepository(connectionString);
            _achievementRepository = new CharacterAchievementRepository(connectionString);
            _staticData = TitleBookStaticDataProvider.LoadDefault();
        }

        public TitleBookMutationResult PutTitle(
            int characterId,
            int accountId,
            InventoryListType sourceList,
            short sourceSlot,
            int itemId,
            int category,
            int bookIndex)
        {
            var result = CreateMutationResult(sourceList, sourceSlot, itemId, category, bookIndex);
            if (!IsCategoryIndexValid(category, bookIndex))
                return result;

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                if (sourceList == InventoryListType.Equipment)
                    return PutEquippedTitle(connection, transaction, characterId, sourceSlot, itemId, category, bookIndex, result);

                var sourceRecord = LoadRecord(connection, transaction, characterId, accountId, sourceList, sourceSlot);
                if (sourceRecord == null || sourceRecord.ItemTemplateId != itemId)
                    return result;

                var definition = _staticData.GetSlot(category, bookIndex);
                if (!definition.AllowsItem(itemId))
                    return result;

                var existing = _titleBookRepository.LoadItem(connection, transaction, characterId, category, bookIndex);
                DeleteRecord(connection, transaction, sourceList, sourceRecord);

                if (existing != null && !existing.IsEmpty)
                {
                    InsertTitleItem(connection, transaction, characterId, accountId, sourceList, sourceSlot, existing);
                    result.ItemLockChanged |= MoveEquipmentLock(connection, transaction, characterId, existing.EquipmentLockId, sourceList, sourceSlot);
                }

                var titleItem = TitleBookInventoryItemCodec.FromItemRecord(category, (ushort)bookIndex, sourceRecord);
                titleItem.Slot = (ushort)bookIndex;
                titleItem.BookIndex = (ushort)bookIndex;
                _titleBookRepository.SaveItem(connection, transaction, characterId, titleItem);
                result.ItemLockChanged |= MoveEquipmentLockToTitleBook(connection, transaction, characterId, titleItem);

                transaction.Commit();
                result.Success = true;
                result.InventoryChanged = true;
                return result;
            }
        }

        private TitleBookMutationResult PutEquippedTitle(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            short sourceSlot,
            int itemId,
            int category,
            int bookIndex,
            TitleBookMutationResult result)
        {
            var equipped = LoadEquippedTitle(connection, transaction, characterId, sourceSlot);
            if (equipped == null || equipped.ItemId != itemId)
            {
                result.ErrorCode = 0x02;
                return result;
            }

            if (!TryResolveTitleBookSlotForItem(itemId, category, bookIndex, out var resolvedCategory, out var resolvedBookIndex))
            {
                result.ErrorCode = 0x02;
                return result;
            }

            var existing = _titleBookRepository.LoadItem(connection, transaction, characterId, resolvedCategory, resolvedBookIndex);
            if (existing != null && !existing.IsEmpty && existing.ItemId != itemId)
            {
                result.ErrorCode = 0x02;
                return result;
            }

            var titleItem = FromEquippedTitle(resolvedCategory, (ushort)resolvedBookIndex, equipped);
            if (existing != null && existing.EquipmentLockId != 0 && existing.EquipmentLockId != titleItem.EquipmentLockId)
                result.ItemLockChanged |= DeleteEquipmentLock(connection, transaction, characterId, existing.EquipmentLockId);
            _titleBookRepository.SaveItem(connection, transaction, characterId, titleItem);
            DeleteEquippedTitle(connection, transaction, characterId, sourceSlot);
            result.ItemLockChanged |= MoveEquipmentLockToTitleBook(connection, transaction, characterId, titleItem);

            transaction.Commit();
            result.Success = true;
            result.Category = resolvedCategory;
            result.BookIndex = resolvedBookIndex;
            result.EquipmentChanged = true;
            return result;
        }

        public TitleBookMutationResult GetTitle(
            int characterId,
            int accountId,
            InventoryListType targetList,
            short targetSlot,
            int itemId,
            int category,
            int bookIndex)
        {
            var result = CreateMutationResult(targetList, targetSlot, itemId, category, bookIndex);
            if (!IsCategoryIndexValid(category, bookIndex))
                return result;

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var title = _titleBookRepository.LoadItem(connection, transaction, characterId, category, bookIndex);
                if (title == null || title.IsEmpty || title.ItemId != itemId)
                    return result;

                if (targetList == InventoryListType.Equipment)
                    return EquipTitleFromBook(connection, transaction, characterId, accountId, title, targetSlot, result);

                var definition = _staticData.GetSlot(category, bookIndex);
                if (definition.QuestId != -1)
                    return result;

                if (LoadRecord(connection, transaction, characterId, accountId, targetList, targetSlot) != null)
                    return result;

                InsertTitleItem(connection, transaction, characterId, accountId, targetList, targetSlot, title);
                _titleBookRepository.ClearItem(connection, transaction, characterId, category, bookIndex);
                result.ItemLockChanged |= MoveEquipmentLock(connection, transaction, characterId, title.EquipmentLockId, targetList, targetSlot);

                transaction.Commit();
                result.Success = true;
                result.InventoryChanged = true;
                return result;
            }
        }

        public AchievementTriggerResult TriggerAchievement(int characterId, int questId, ushort delta1, ushort delta2, ushort delta3)
        {
            var result = new AchievementTriggerResult { QuestId = questId };
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var quest = _staticData.GetQuest(questId);
                var entry = _achievementRepository.LoadOrCreateEntry(
                    connection,
                    transaction,
                    characterId,
                    questId,
                    quest != null ? quest.CheckCount : (ushort)1);
                entry.P1 = SaturatingSubtract(entry.P1, delta1);
                entry.P2 = SaturatingSubtract(entry.P2, delta2);
                entry.P3 = SaturatingSubtract(entry.P3, delta3);
                _achievementRepository.SaveEntry(connection, transaction, characterId, entry);

                result.Remain1 = entry.P1;
                result.Remain2 = entry.P2;
                result.Remain3 = entry.P3;
                result.TailOrState = entry.P4;
                result.Success = true;
                result.Completed = entry.P1 == 0 && entry.P2 == 0 && entry.P3 == 0;

                if (result.Completed && _staticData.TryFindByQuestId(questId, out var definition))
                {
                    var titleItemId = quest != null && quest.RewardTitleItemId > 0
                        ? quest.RewardTitleItemId
                        : definition.AllowedTitleItemIds.FirstOrDefault(id => id > 0);
                    if (titleItemId > 0)
                    {
                        var title = new TitleBookInventoryItem
                        {
                            Category = definition.Category,
                            BookIndex = (ushort)definition.Index,
                            Slot = (ushort)definition.Index,
                            ItemId = titleItemId,
                            Value = titleItemId,
                        };
                        _titleBookRepository.SaveItem(connection, transaction, characterId, title);
                        result.Category = definition.Category;
                        result.BookIndex = definition.Index;
                        result.TitleItemId = titleItemId;
                    }
                }

                transaction.Commit();
                return result;
            }
        }

        private TitleBookMutationResult EquipTitleFromBook(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int accountId,
            TitleBookInventoryItem title,
            short equipSlot,
            TitleBookMutationResult result)
        {
            if (LoadEquippedTitle(connection, transaction, characterId, equipSlot) != null)
            {
                result.ErrorCode = 0x02;
                return result;
            }

            InsertEquippedTitle(connection, transaction, characterId, equipSlot, title);
            _titleBookRepository.ClearItem(connection, transaction, characterId, title.Category, title.BookIndex);
            result.ItemLockChanged |= MoveEquipmentLock(connection, transaction, characterId, title.EquipmentLockId, InventoryListType.Equipment, equipSlot);

            transaction.Commit();
            result.Success = true;
            result.Category = title.Category;
            result.BookIndex = title.BookIndex;
            result.EquipmentChanged = true;
            return result;
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static TitleBookMutationResult CreateMutationResult(InventoryListType itemSpace, short slotIndex, int itemId, int category, int bookIndex)
        {
            return new TitleBookMutationResult
            {
                ItemSpace = itemSpace,
                SlotIndex = slotIndex,
                ItemId = itemId,
                Category = category,
                BookIndex = bookIndex,
            };
        }

        private static bool IsCategoryIndexValid(int category, int bookIndex)
        {
            return category >= 0
                && category < TitleBookStaticDataProvider.CategoryCapacities.Count
                && bookIndex >= 0
                && bookIndex < TitleBookStaticDataProvider.CategoryCapacities[category];
        }

        private SqliteInventoryStore.ItemRecord LoadRecord(SqliteConnection connection, SqliteTransaction transaction, int characterId, int accountId, InventoryListType listType, short slotIndex)
        {
            if (listType == InventoryListType.AccountCargo)
                return _db.LoadAccountCargoItemRecord(connection, transaction, accountId, slotIndex);

            if (listType == InventoryListType.Equipment || listType == InventoryListType.Pet)
                return null;

            return _db.LoadItemRecord(connection, transaction, characterId, SqliteInventoryStore.MapToDbListType(listType), slotIndex);
        }

        private void DeleteRecord(SqliteConnection connection, SqliteTransaction transaction, InventoryListType listType, SqliteInventoryStore.ItemRecord record)
        {
            if (listType == InventoryListType.AccountCargo)
                _db.DeleteAccountCargoItem(connection, transaction, record.ItemUid);
            else
                _db.DeleteItem(connection, transaction, record.ItemUid);
        }

        private void InsertTitleItem(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int accountId,
            InventoryListType listType,
            short slotIndex,
            TitleBookInventoryItem item)
        {
            if (listType == InventoryListType.AccountCargo)
            {
                InsertAccountCargoTitleItem(connection, transaction, accountId, slotIndex, item);
                return;
            }

            var dbListType = SqliteInventoryStore.MapToDbListType(listType);
            _db.InsertCharacterItem(
                connection,
                transaction,
                characterId,
                dbListType,
                slotIndex,
                item.ItemId,
                TitleBookInventoryItemCodec.InferItemKind(item),
                item.Value,
                item.Value,
                item.Durability,
                item.SealFlag,
                0,
                item.ExpireTime,
                item.Marker16,
                0,
                TitleBookInventoryItemCodec.ToExtraJson(item),
                item.EquipmentLockId);
        }

        // SQL 与 InventoryDbPrimitives.InsertAccountCargoItem 同表同列，改表结构时两处须同步。
        private static void InsertAccountCargoTitleItem(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int accountId,
            short slotIndex,
            TitleBookInventoryItem item)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT OR REPLACE INTO account_cargo_items (
    account_id, slot_index, item_template_id, item_kind,
    stack_count, instance_value, durability, seal_flag, option_value, expire_time, marker_16,
    extra_json)
VALUES (
    @accountId, @slotIndex, @templateId, @itemKind,
    @stackCount, @instanceValue, @durability, @sealFlag, @optionValue, @expireTime, @marker16,
    @extraJson);";
                command.Parameters.AddWithValue("@accountId", accountId);
                command.Parameters.AddWithValue("@slotIndex", slotIndex);
                command.Parameters.AddWithValue("@templateId", item.ItemId);
                command.Parameters.AddWithValue("@itemKind", TitleBookInventoryItemCodec.InferItemKind(item));
                command.Parameters.AddWithValue("@stackCount", item.Value);
                command.Parameters.AddWithValue("@instanceValue", item.Value);
                command.Parameters.AddWithValue("@durability", item.Durability);
                command.Parameters.AddWithValue("@sealFlag", item.SealFlag);
                command.Parameters.AddWithValue("@optionValue", 0);
                command.Parameters.AddWithValue("@expireTime", item.ExpireTime);
                command.Parameters.AddWithValue("@marker16", item.Marker16);
                command.Parameters.AddWithValue("@extraJson", TitleBookInventoryItemCodec.ToExtraJson(item));
                command.ExecuteNonQuery();
            }
        }

        private static EquippedTitleRecord LoadEquippedTitle(SqliteConnection connection, SqliteTransaction transaction, int characterId, short slot)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT slot, item_id, expire_time, equipment_lock_id, raw_entry
FROM character_equipped_entries
WHERE character_id = @cid AND slot = @slot;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@slot", slot);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return new EquippedTitleRecord
                    {
                        Slot = reader.GetInt32(0),
                        ItemId = reader.GetInt32(1),
                        ExpireTime = reader.GetInt32(2),
                        EquipmentLockId = unchecked((byte)reader.GetInt32(3)),
                        Raw = (byte[])reader["raw_entry"],
                    };
                }
            }
        }

        private static void DeleteEquippedTitle(SqliteConnection connection, SqliteTransaction transaction, int characterId, short slot)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM character_equipped_entries WHERE character_id = @cid AND slot = @slot;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@slot", slot);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertEquippedTitle(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            short slot,
            TitleBookInventoryItem title)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO character_equipped_entries(character_id, slot, item_id, expire_time, equipment_lock_id, raw_entry)
VALUES(@cid, @slot, @itemId, @expireTime, @equipmentLockId, @raw);";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@slot", (int)slot);
                command.Parameters.AddWithValue("@itemId", title.ItemId);
                command.Parameters.AddWithValue("@expireTime", title.ExpireTime);
                command.Parameters.AddWithValue("@equipmentLockId", (int)title.EquipmentLockId);
                command.Parameters.AddWithValue("@raw", BuildEquippedRawFromTitleBook(title, slot));
                command.ExecuteNonQuery();
            }
        }

        private bool TryResolveTitleBookSlotForItem(
            int itemId,
            int requestedCategory,
            int requestedBookIndex,
            out int category,
            out int bookIndex)
        {
            if (IsCategoryIndexValid(requestedCategory, requestedBookIndex)
                && _staticData.GetSlot(requestedCategory, requestedBookIndex).AllowsItem(itemId))
            {
                category = requestedCategory;
                bookIndex = requestedBookIndex;
                return true;
            }

            for (category = 0; category < TitleBookStaticDataProvider.CategoryCapacities.Count; category++)
            {
                var capacity = TitleBookStaticDataProvider.CategoryCapacities[category];
                for (bookIndex = 0; bookIndex < capacity; bookIndex++)
                {
                    if (_staticData.GetSlot(category, bookIndex).AllowsItem(itemId))
                        return true;
                }
            }

            category = -1;
            bookIndex = -1;
            return false;
        }

        private static TitleBookInventoryItem FromEquippedTitle(int category, ushort bookIndex, EquippedTitleRecord equipped)
        {
            var fields = MakeEquipListCodec.ParseDisplayFields(equipped.Raw);
            return new TitleBookInventoryItem
            {
                Category = category,
                BookIndex = bookIndex,
                Slot = bookIndex,
                ItemId = equipped.ItemId,
                Value = unchecked((int)fields.InstanceValue),
                Attr = fields.Reinforce,
                Durability = fields.Durability,
                SealFlag = 0,
                EnchantIndex = unchecked((int)fields.Enchant),
                EnchantUpgradeCount = fields.EnchantUpgradeCount,
                AmplifyType = fields.AmplifyType,
                AmplifyValue = fields.AmplifyValue,
                Marker16 = -1,
                ExpireTime = equipped.ExpireTime,
                TailData = BuildTitleBookTail(fields),
                EquipmentLockId = equipped.EquipmentLockId,
            };
        }

        private static byte[] BuildEquippedRawFromTitleBook(TitleBookInventoryItem title, short equipSlot)
        {
            return MakeEquipListCodec.BuildEntryFromDisplayFields(
                equipSlot,
                title.ItemId,
                BuildDisplayFields(title));
        }

        private static MakeEquipListCodec.DisplayFields BuildDisplayFields(TitleBookInventoryItem title)
        {
            var tail = title.TailData ?? Array.Empty<byte>();
            var fields = new MakeEquipListCodec.DisplayFields
            {
                InstanceValue = unchecked((uint)title.Value),
                Reinforce = title.Attr,
                Durability = title.Durability,
                Enchant = unchecked((uint)title.EnchantIndex),
                EnchantUpgradeCount = title.EnchantUpgradeCount,
                AmplifyType = title.AmplifyType,
                AmplifyValue = title.AmplifyValue,
                MagicSealTypes = new byte[3],
                MagicSealVal1s = new byte[3],
                MagicSealVal2s = new byte[3],
                MagicSealTail = Array.Empty<byte>(),
            };

            if (tail.Length > 0)
            {
                var emblemLen = 1 + tail[0] * 4;
                if (emblemLen > 0 && emblemLen <= tail.Length)
                {
                    fields.Emblem = new byte[emblemLen];
                    Buffer.BlockCopy(tail, 0, fields.Emblem, 0, emblemLen);
                }
            }

            if (tail.Length >= 11)
                fields.Rune = BitConverter.ToUInt16(tail, 9);
            if (tail.Length > 27)
                fields.Forging = tail[27];
            if (tail.Length > 11)
            {
                fields.MagicSealCount = tail[11];
                for (var i = 0; i < fields.MagicSealCount && i < 3; i++)
                {
                    if (12 + i < tail.Length) fields.MagicSealTypes[i] = tail[12 + i];
                    if (15 + i < tail.Length) fields.MagicSealVal1s[i] = tail[15 + i];
                    if (18 + i < tail.Length) fields.MagicSealVal2s[i] = tail[18 + i];
                }

                if (fields.MagicSealCount > 0 && tail.Length > 21)
                {
                    var sealTailLen = Math.Min(6, tail.Length - 21);
                    fields.MagicSealTail = new byte[sealTailLen];
                    Buffer.BlockCopy(tail, 21, fields.MagicSealTail, 0, sealTailLen);
                }
            }

            return fields;
        }

        private static byte[] BuildTitleBookTail(MakeEquipListCodec.DisplayFields fields)
        {
            var tail = new byte[37];
            if (fields.Emblem != null && fields.Emblem.Length > 0)
                Buffer.BlockCopy(fields.Emblem, 0, tail, 0, Math.Min(fields.Emblem.Length, 9));

            BitConverter.GetBytes(fields.Rune).CopyTo(tail, 9);
            tail[27] = fields.Forging;
            if (fields.MagicSealCount > 0 && fields.MagicSealTypes != null)
            {
                tail[11] = fields.MagicSealCount;
                for (var i = 0; i < fields.MagicSealCount && i < 3; i++)
                {
                    tail[12 + i] = fields.MagicSealTypes[i];
                    tail[15 + i] = fields.MagicSealVal1s[i];
                    tail[18 + i] = fields.MagicSealVal2s[i];
                }

                if (fields.MagicSealTail != null)
                {
                    for (var i = 0; i < fields.MagicSealTail.Length && 21 + i < tail.Length; i++)
                        tail[21 + i] = fields.MagicSealTail[i];
                }
            }

            return tail;
        }

        private static bool MoveEquipmentLockToTitleBook(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            TitleBookInventoryItem item)
        {
            if (item == null)
                return false;

            return MoveEquipmentLock(
                connection,
                transaction,
                characterId,
                item.EquipmentLockId,
                GetTitleBookLockListType(item.Category),
                unchecked((short)item.BookIndex));
        }

        private static InventoryListType GetTitleBookLockListType(int category)
        {
            return (InventoryListType)(TitleBookLockListTypeBase + category);
        }

        private static bool MoveEquipmentLock(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte equipmentLockId,
            InventoryListType listType,
            short slot)
        {
            if (equipmentLockId == 0)
                return false;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO character_item_locks (
    character_id, equipment_lock_id, inventory_list_type, slot, state, remaining_seconds)
VALUES (@cid, @lockId, @listType, @slot, 1, NULL)
ON CONFLICT(character_id, equipment_lock_id)
DO UPDATE SET
    inventory_list_type = excluded.inventory_list_type,
    slot = excluded.slot;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@lockId", (int)equipmentLockId);
                command.Parameters.AddWithValue("@listType", (int)listType);
                command.Parameters.AddWithValue("@slot", (int)slot);
                command.ExecuteNonQuery();
            }

            return true;
        }

        private static bool DeleteEquipmentLock(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte equipmentLockId)
        {
            if (equipmentLockId == 0)
                return false;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
DELETE FROM character_item_locks
WHERE character_id = @cid AND equipment_lock_id = @lockId;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@lockId", (int)equipmentLockId);
                command.ExecuteNonQuery();
            }

            return true;
        }

        private static ushort SaturatingSubtract(ushort value, ushort delta)
        {
            return delta >= value ? (ushort)0 : (ushort)(value - delta);
        }

        private sealed class EquippedTitleRecord
        {
            public int Slot { get; set; }

            public int ItemId { get; set; }

            public int ExpireTime { get; set; }

            public byte EquipmentLockId { get; set; }

            public byte[] Raw { get; set; }
        }
    }
}
