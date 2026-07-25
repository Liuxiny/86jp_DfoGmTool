using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class LegacyItemCoreConverter
    {
        internal static void Migrate(SqliteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            using (var transaction = connection.BeginTransaction())
            {
                EnsureTables(connection, transaction);
                ExecuteNonQuery(connection, transaction, @"
DELETE FROM character_avatar_detail;
DELETE FROM character_new_items;");

                foreach (var row in ReadCharacterItemRows(connection, transaction))
                {
                    if (ShouldSkipCharacterNewItem(row))
                        continue;

                    var core = BuildCoreFromCharacterItem(row);
                    long avatarUid = 0;
                    if (core.ItemKind == ItemCore.KindAvatar)
                    {
                        avatarUid = AllocateAvatarUid(connection, transaction);
                        core.AvatarUid = ToInt32(avatarUid);
                    }

                    InsertNewItem(connection, transaction, row.ItemUid, row.OwnerScope, row.OwnerId, row.CharacterId, row.ListType, row.SlotIndex, core, row.CreatedAt, row.UpdatedAt);

                    if (core.ItemKind == ItemCore.KindAvatar)
                        InsertAvatarDetail(connection, transaction, BuildAvatarDetailFromCharacterItem(row, core, avatarUid));
                }

                foreach (var row in ReadEquippedRows(connection, transaction))
                {
                    if (!TryResolveEquippedItemKind(row.SlotIndex, out var itemKind))
                        continue;

                    var fields = MakeEquipListCodec.ParseDisplayFields(row.RawEntry);
                    var core = BuildCoreFromEquippedEntry(row, itemKind, fields);
                    long avatarUid = 0;
                    if (itemKind == ItemCore.KindAvatar)
                    {
                        avatarUid = AllocateAvatarUid(connection, transaction);
                        core.AvatarUid = ToInt32(avatarUid);
                    }

                    InsertNewItem(connection, transaction, null, "character", row.CharacterId, row.CharacterId, InventoryListType.Equipment, row.SlotIndex, core, null, null);

                    if (itemKind == ItemCore.KindAvatar)
                        InsertAvatarDetail(connection, transaction, BuildAvatarDetailFromEquippedEntry(row, avatarUid, fields));
                }

                RebuildAccountCargoNewItems(connection, transaction);

                transaction.Commit();
            }
        }

        internal static void MigrateAccountCargo(SqliteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            using (var transaction = connection.BeginTransaction())
            {
                EnsureTables(connection, transaction);
                RebuildAccountCargoNewItems(connection, transaction);
                transaction.Commit();
            }
        }

        internal static void MigrateMainVirtualCurrencySlots(SqliteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            using (var transaction = connection.BeginTransaction())
            {
                EnsureTables(connection, transaction);
                foreach (var row in ReadLegacyMainVirtualCurrencyRows(connection, transaction))
                    InventoryMainVirtualCountRepository.UpsertCurrencySlot(
                        connection,
                        transaction,
                        row.CharacterId,
                        row.SlotIndex,
                        row.Count);

                transaction.Commit();
            }
        }
        private static void RebuildAccountCargoNewItems(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteNonQuery(connection, transaction, "DELETE FROM account_cargo_new_items;");

            foreach (var row in ReadAccountCargoItemRows(connection, transaction))
            {
                var core = BuildCoreFromAccountCargoItem(row);
                InsertAccountCargoNewItem(connection, transaction, row.ItemUid, row.AccountId, row.CharacterId, InventoryListType.AccountCargo, row.SlotIndex, core, row.CreatedAt, row.UpdatedAt);
            }
        }

        private static bool ShouldSkipCharacterNewItem(CharacterItemRow row)
        {
            return row.ListType == InventoryListType.Main
                && (IsMainVirtualCubeSlot(row.SlotIndex)
                    || InventoryService.IsReservedMainSlot(row.SlotIndex));
        }

        private static void EnsureTables(SqliteConnection connection, SqliteTransaction transaction)
        {
            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS character_new_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')),
    owner_id INTEGER NOT NULL,
    character_id INTEGER,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB NOT NULL CHECK(length(item_core) = 82),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(owner_scope, owner_id, list_type, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_character_new_items_character_space
    ON character_new_items(character_id, list_type, slot_index);

CREATE TABLE IF NOT EXISTS character_avatar_detail (
    item_uid INTEGER PRIMARY KEY,
    owner_id INTEGER NOT NULL DEFAULT 0,
    character_id INTEGER NOT NULL DEFAULT 0,
    item_id INTEGER NOT NULL DEFAULT 0,
    expire_date INTEGER NOT NULL DEFAULT 0,
    clear_avatar_id INTEGER NOT NULL DEFAULT 0,
    jewel_socket BLOB NOT NULL CHECK(length(jewel_socket) = 30),
    color1 INTEGER NOT NULL DEFAULT 0,
    color2 INTEGER NOT NULL DEFAULT 0,
    delete_date INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_character_avatar_detail_character
    ON character_avatar_detail(character_id);

CREATE TABLE IF NOT EXISTS character_avatar_uid_sequence (
    avatar_uid INTEGER PRIMARY KEY AUTOINCREMENT
);

CREATE TABLE IF NOT EXISTS character_creature_uid_sequence (
    creature_uid INTEGER PRIMARY KEY AUTOINCREMENT
);

CREATE TABLE IF NOT EXISTS account_cargo_new_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id INTEGER NOT NULL,
    character_id INTEGER,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB NOT NULL CHECK(length(item_core) = 82),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(account_id, slot_index),
    FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);");
        }

        internal static List<CharacterItemRow> ReadCharacterItemRows(SqliteConnection connection, SqliteTransaction transaction)
        {
            var rows = new List<CharacterItemRow>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT item_uid,
       owner_scope,
       owner_id,
       character_id,
       list_type,
       slot_index,
       item_template_id,
       item_kind,
       stack_count,
       instance_value,
       durability,
       seal_flag,
       option_value,
       equipment_lock_id,
       expire_time,
       marker_16,
       pet_serial_or_handle,
       extra_json,
       created_at,
       updated_at
FROM character_items;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new CharacterItemRow
                        {
                            ItemUid = reader.GetInt64(0),
                            OwnerScope = reader.GetString(1),
                            OwnerId = reader.GetInt32(2),
                            CharacterId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                            ListType = (InventoryListType)reader.GetInt32(4),
                            SlotIndex = Convert.ToInt16(reader.GetInt32(5), CultureInfo.InvariantCulture),
                            ItemTemplateId = reader.GetInt32(6),
                            ItemKindText = reader.GetString(7),
                            StackCount = reader.GetInt32(8),
                            InstanceValue = reader.GetInt32(9),
                            Durability = Convert.ToUInt16(reader.GetInt32(10), CultureInfo.InvariantCulture),
                            SealFlag = Convert.ToByte(reader.GetInt32(11), CultureInfo.InvariantCulture),
                            OptionValue = Convert.ToByte(reader.GetInt32(12), CultureInfo.InvariantCulture),
                            EquipmentLockId = Convert.ToByte(reader.GetInt32(13), CultureInfo.InvariantCulture),
                            ExpireTime = reader.GetInt32(14),
                            Marker16 = reader.GetInt32(15),
                            PetSerialOrHandle = reader.GetInt32(16),
                            ExtraJson = reader.IsDBNull(17) ? "{}" : reader.GetString(17),
                            CreatedAt = reader.GetString(18),
                            UpdatedAt = reader.GetString(19),
                        });
                    }
                }
            }

            return rows;
        }

        private static List<MainVirtualCurrencyRow> ReadLegacyMainVirtualCurrencyRows(SqliteConnection connection, SqliteTransaction transaction)
        {
            var rows = new List<MainVirtualCurrencyRow>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COALESCE(character_id, owner_id) AS character_id,
       slot_index,
       MAX(CASE WHEN stack_count >= instance_value THEN stack_count ELSE instance_value END) AS stack_count
FROM character_items
WHERE owner_scope = 'character'
  AND list_type = 0
  AND slot_index IN (0, 1, 2)
GROUP BY COALESCE(character_id, owner_id), slot_index;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new MainVirtualCurrencyRow
                        {
                            CharacterId = reader.GetInt32(0),
                            SlotIndex = Convert.ToInt16(reader.GetInt32(1), CultureInfo.InvariantCulture),
                            Count = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        });
                    }
                }
            }

            return rows;
        }

        internal static List<EquippedEntryRow> ReadEquippedRows(SqliteConnection connection, SqliteTransaction transaction)
        {
            var rows = new List<EquippedEntryRow>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT character_id,
       slot,
       item_id,
       expire_time,
       equipment_lock_id,
       raw_entry
FROM character_equipped_entries;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new EquippedEntryRow
                        {
                            CharacterId = reader.GetInt32(0),
                            SlotIndex = Convert.ToInt16(reader.GetInt32(1), CultureInfo.InvariantCulture),
                            ItemTemplateId = reader.GetInt32(2),
                            ExpireTime = reader.GetInt32(3),
                            EquipmentLockId = Convert.ToByte(reader.GetInt32(4), CultureInfo.InvariantCulture),
                            RawEntry = reader.IsDBNull(5) ? Array.Empty<byte>() : (byte[])reader[5],
                        });
                    }
                }
            }

            return rows;
        }

        internal static List<AccountCargoItemRow> ReadAccountCargoItemRows(SqliteConnection connection, SqliteTransaction transaction)
        {
            var rows = new List<AccountCargoItemRow>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT item_uid,
       account_id,
       slot_index,
       item_template_id,
       item_kind,
       stack_count,
       instance_value,
       durability,
       seal_flag,
       option_value,
       expire_time,
       marker_16,
       extra_json,
       created_at,
       updated_at
FROM account_cargo_items;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new AccountCargoItemRow
                        {
                            ItemUid = reader.GetInt64(0),
                            AccountId = reader.GetInt32(1),
                            CharacterId = 0,
                            SlotIndex = Convert.ToInt16(reader.GetInt32(2), CultureInfo.InvariantCulture),
                            ItemTemplateId = reader.GetInt32(3),
                            ItemKindText = reader.GetString(4),
                            StackCount = reader.GetInt32(5),
                            InstanceValue = reader.GetInt32(6),
                            Durability = Convert.ToUInt16(reader.GetInt32(7), CultureInfo.InvariantCulture),
                            SealFlag = Convert.ToByte(reader.GetInt32(8), CultureInfo.InvariantCulture),
                            OptionValue = Convert.ToByte(reader.GetInt32(9), CultureInfo.InvariantCulture),
                            ExpireTime = reader.GetInt32(10),
                            Marker16 = reader.GetInt32(11),
                            ExtraJson = reader.IsDBNull(12) ? "{}" : reader.GetString(12),
                            CreatedAt = reader.GetString(13),
                            UpdatedAt = reader.GetString(14),
                        });
                    }
                }
            }

            return rows;
        }

        internal static ItemCore BuildCoreFromCharacterItem(CharacterItemRow row)
        {
            if (row.ListType == InventoryListType.Main && IsMainVirtualCurrencySlot(row.SlotIndex))
                return BuildMainVirtualCurrencyCore(row.SlotIndex, Math.Max(row.StackCount, row.InstanceValue));

            var payload = LegacyExtraPayload.Parse(row.ExtraJson);
            var itemKind = ResolveItemKind(row);
            var core = ItemCore.Create(itemKind, row.ItemTemplateId);
            core.EquipmentLockId = row.EquipmentLockId;

            if (itemKind == ItemCore.KindAvatar)
            {
                ApplyAvatarCharacterItemPayload(core, row, payload);
                return core;
            }

            if (row.ListType == InventoryListType.Pet)
            {
                ApplyPetCharacterItemPayload(core, row, payload);
                return core;
            }

            ApplyCommonCharacterItemPayload(core, row, payload);
            return core;
        }

        private static ItemCore BuildMainVirtualCurrencyCore(short slotIndex, int count)
        {
            return new ItemCore
            {
                ItemKind = ItemCore.KindSpecialMaterial,
                ItemId = slotIndex,
                Count = Math.Max(0, count),
            };
        }

        internal static ItemCore BuildCoreFromAccountCargoItem(AccountCargoItemRow row)
        {
            var payload = LegacyExtraPayload.Parse(row.ExtraJson);
            var itemKind = ResolveAccountCargoItemKind(row);
            var core = ItemCore.Create(itemKind, row.ItemTemplateId);
            ApplyAccountCargoItemPayload(core, row, payload);
            return core;
        }

        internal static ItemCore BuildCoreFromEquippedEntry(EquippedEntryRow row, byte itemKind, MakeEquipListCodec.DisplayFields fields)
        {
            var core = ItemCore.Create(itemKind, row.ItemTemplateId);
            core.Value = unchecked((int)fields.InstanceValue);
            core.Attr = fields.Reinforce;
            core.Durability = fields.Durability;
            core.SealFlag = fields.SealFlag;
            core.EnchantCardId = unchecked((int)fields.Enchant);
            core.EnchantUpgradeCount = fields.EnchantUpgradeCount;
            core.AmplifyType = fields.AmplifyType;
            core.AmplifyValue = fields.AmplifyValue;
            core.ExpireTime = row.ExpireTime != 0 ? row.ExpireTime : fields.ExpireTime;
            core.EquipmentLockId = row.EquipmentLockId;

            if (itemKind == ItemCore.KindCreature)
                core.Marker16 = fields.Marker16 == 0 ? ItemCore.Marker16Default : unchecked((int)fields.Marker16);

            ApplyChronicleOptions(core, fields.ChronicleOptions);
            core.EmblemSocketCount = fields.EmblemSocketCount;
            core.EmblemId1 = fields.EmblemId1;
            core.EmblemId2 = fields.EmblemId2;
            core.Rune = fields.Rune;
            ApplyRandomOptions(core, fields);
            core.RandomOptionState = fields.RandomOptionState;
            core.RandomOptionChangedIndex = fields.RandomOptionChangedIndex;
            core.RandomOptionChangeState = fields.RandomOptionChangeState;
            core.RandomOptionChange.Type = fields.RandomOptionChangeType;
            core.RandomOptionChange.Value1 = fields.RandomOptionChangeValue1;
            core.RandomOptionChange.Value2 = fields.RandomOptionChangeValue2;
            core.GenuineUpgrade = fields.Forging;
            core.EmancipateEquipmentLevel = fields.EmancipateEquipmentLevel;
            core.TradeRestriction = fields.TradeRestriction;
            core.TailUnknown0 = fields.TailUnknown0;
            core.TailUnknown1 = fields.TailUnknown1;
            core.TailUnknown2 = fields.TailUnknown2;
            core.TailUnknown3 = fields.TailUnknown3;
            core.RemainUseCount = fields.RemainUseCount;
            core.SortLockFlag = fields.SortLockFlag;
            return core;
        }

        private static void ApplyCommonCharacterItemPayload(ItemCore core, CharacterItemRow row, LegacyExtraPayload payload)
        {
            core.Value = IsStackCountItemKind(core.ItemKind) ? row.StackCount : row.InstanceValue;
            core.Attr = payload.ExtData0;
            core.Durability = row.Durability;
            core.SealFlag = row.SealFlag;
            ApplyPrefixData(core, payload.PrefixData0E);
            core.Marker16 = row.Marker16 == 0 ? ItemCore.Marker16Default : row.Marker16;
            ApplyMiddleData(core, payload.MiddleData1A);
            core.ExpireTime = row.ExpireTime;
            ApplyTailData(core, payload.TailData2F);
        }

        private static void ApplyAccountCargoItemPayload(ItemCore core, AccountCargoItemRow row, LegacyExtraPayload payload)
        {
            core.Value = IsStackCountItemKind(core.ItemKind) ? row.StackCount : row.InstanceValue;
            core.Attr = payload.ExtData0;
            core.Durability = row.Durability;
            core.SealFlag = row.SealFlag;
            ApplyPrefixData(core, payload.PrefixData0E);
            core.Marker16 = row.Marker16 == 0 ? ItemCore.Marker16Default : row.Marker16;
            ApplyMiddleData(core, payload.MiddleData1A);
            core.ExpireTime = row.ExpireTime;
            ApplyTailData(core, payload.TailData2F);
        }

        private static void ApplyAvatarCharacterItemPayload(ItemCore core, CharacterItemRow row, LegacyExtraPayload payload)
        {
            core.AvatarUid = 0;
            core.Attr = ReadByte(payload.AvatarReserved0, 4);
            core.Durability = Convert.ToUInt16(row.OptionValue | (ReadByte(payload.AvatarReserved1, 0) << 8), CultureInfo.InvariantCulture);
            core.SealFlag = ReadByte(payload.AvatarReserved1, 1);
            ApplyPrefixData(core, payload.AvatarReserved1, 2);
            core.Marker16 = NormalizeMarker16(ReadInt32(payload.AvatarReserved1, 10));
            ApplyMiddleData(core, payload.AvatarReserved1, 14);
            core.ExpireTime = ReadInt32(payload.AvatarReserved1, 31);
            ApplyTailData(core, payload.AvatarReserved1, 35);
            core.SortLockFlag = Convert.ToByte(row.Marker16 & 0xFF, CultureInfo.InvariantCulture);
        }

        private static void ApplyPetCharacterItemPayload(ItemCore core, CharacterItemRow row, LegacyExtraPayload payload)
        {
            core.Value = row.PetSerialOrHandle;
            core.Attr = ReadByte(payload.PetTailData0A, 0);
            core.Durability = ReadUInt16(payload.PetTailData0A, 1);
            core.SealFlag = ReadByte(payload.PetTailData0A, 3);
            ApplyPrefixData(core, payload.PetTailData0A, 4);
            core.Marker16 = NormalizeMarker16(ReadInt32(payload.PetTailData0A, 12));
            ApplyMiddleData(core, payload.PetTailData0A, 16);
            core.ExpireTime = ReadInt32(payload.PetTailData0A, 33);
            ApplyTailData(core, payload.PetTailData0A, 37);
        }

        internal static AvatarDetail BuildAvatarDetailFromCharacterItem(CharacterItemRow row, ItemCore core, long avatarUid)
        {
            var payload = LegacyExtraPayload.Parse(row.ExtraJson);
            return new AvatarDetail
            {
                AvatarUid = avatarUid,
                OwnerId = row.OwnerId,
                CharacterId = row.CharacterId,
                ItemId = row.ItemTemplateId,
                ExpireDate = row.ExpireTime != 0 ? row.ExpireTime : core.ExpireTime,
                ClearAvatarId = 0,
                JewelSocket = AvatarSocketDataCodec.Normalize(payload.AvatarSocketData),
                Color1 = ReadUInt16(payload.AvatarTailData, 0),
                Color2 = ReadUInt16(payload.AvatarTailData, 2),
            };
        }

        internal static AvatarDetail BuildAvatarDetailFromEquippedEntry(EquippedEntryRow row, long avatarUid, MakeEquipListCodec.DisplayFields fields)
        {
            var colorData = CopyFixed(fields.ExpansionData, 4);
            return new AvatarDetail
            {
                AvatarUid = avatarUid,
                OwnerId = row.CharacterId,
                CharacterId = row.CharacterId,
                ItemId = row.ItemTemplateId,
                ExpireDate = row.ExpireTime != 0 ? row.ExpireTime : fields.ExpireTime,
                ClearAvatarId = unchecked((int)fields.ClearAvatarId),
                JewelSocket = AvatarSocketDataCodec.Normalize(fields.JewelSocket),
                Color1 = ReadUInt16(colorData, 0),
                Color2 = ReadUInt16(colorData, 2),
            };
        }

        private static byte ResolveItemKind(CharacterItemRow row)
        {
            if (ItemSlotBoundService.TryResolveItemKindForMigration(row.ListType, row.SlotIndex, row.ItemTemplateId, out var itemKind))
                return itemKind;

            return ResolveLegacyItemKind(row.ItemKindText);
        }

        private static byte ResolveAccountCargoItemKind(AccountCargoItemRow row)
        {
            try
            {
                if (ItemSlotBoundService.TryResolveItemKindForMigration(InventoryListType.AccountCargo, row.SlotIndex, row.ItemTemplateId, out var itemKind))
                    return itemKind;
            }
            catch
            {
                // 离线迁移自测或仅选择数据库时可能没有 PVF；此时使用旧表持久化类型兜底。
            }
            return ResolveLegacyItemKind(row.ItemKindText);
        }

        private static bool TryResolveEquippedItemKind(short slotIndex, out byte itemKind)
        {
            if (slotIndex >= 0 && slotIndex <= 10)
            {
                itemKind = ItemCore.KindAvatar;
                return true;
            }

            if (slotIndex >= 11 && slotIndex <= 23)
            {
                itemKind = ItemCore.KindEquipment;
                return true;
            }

            if (slotIndex == 24)
            {
                itemKind = ItemCore.KindCreature;
                return true;
            }

            if (slotIndex >= 25 && slotIndex <= 27)
            {
                itemKind = ItemCore.KindCreatureEquipment;
                return true;
            }

            itemKind = ItemCore.KindUnknown;
            return false;
        }

        private static byte ResolveLegacyItemKind(string itemKindText)
        {
            switch ((itemKindText ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "equipment":
                    return ItemCore.KindEquipment;
                case "avatar":
                    return ItemCore.KindAvatar;
                case "pet":
                    return ItemCore.KindCreature;
                case "stackable":
                    return ItemCore.KindConsumable;
                case "special":
                    return ItemCore.KindSpecialMaterial;
                default:
                    return ItemCore.KindUnknown;
            }
        }

        private static bool IsStackCountItemKind(byte itemKind)
        {
            return itemKind == ItemCore.KindConsumable
                || itemKind == ItemCore.KindMaterial
                || itemKind == ItemCore.KindQuest
                || itemKind == ItemCore.KindCreatureConsumable
                || itemKind == ItemCore.KindAvatarEmblem
                || itemKind == ItemCore.KindExpertJobMaterial
                || itemKind == ItemCore.KindSpecialMaterial;
        }

        private static bool IsMainVirtualCurrencySlot(short slotIndex)
        {
            return slotIndex >= InventoryService.MainVirtualCurrencySlotStart
                && slotIndex <= InventoryService.MainVirtualCurrencySlotEnd;
        }

        private static bool IsMainVirtualCubeSlot(short slotIndex)
        {
            return slotIndex >= InventoryService.MainVirtualCubeSlotStart
                && slotIndex <= InventoryService.MainVirtualCubeSlotEnd;
        }

        private static void ApplyPrefixData(ItemCore core, byte[] data, int offset = 0)
        {
            core.EnchantCardId = ReadInt32(data, offset);
            core.EnchantUpgradeCount = ReadByte(data, offset + 4);
            core.AmplifyType = ReadByte(data, offset + 5);
            core.AmplifyValue = ReadUInt16(data, offset + 6);
        }

        private static void ApplyMiddleData(ItemCore core, byte[] data, int offset = 0)
        {
            var optionCount = ReadByte(data, offset);
            if (optionCount > 0 || ReadInt32(data, offset + 1) != 0)
            {
                core.ChronicleOption0.OptionId = ReadInt32(data, offset + 1);
                core.ChronicleOption0.CharacJob = ReadByte(data, offset + 9);
                core.ChronicleOption0.FirstGrowType = ReadByte(data, offset + 11);
                core.ChronicleOption0.EquipmentType = ReadByte(data, offset + 13);
                core.ChronicleOption0.OptionNo = ReadByte(data, offset + 15);
            }

            if (optionCount > 1 || ReadInt32(data, offset + 5) != 0)
            {
                core.ChronicleOption1.OptionId = ReadInt32(data, offset + 5);
                core.ChronicleOption1.CharacJob = ReadByte(data, offset + 10);
                core.ChronicleOption1.FirstGrowType = ReadByte(data, offset + 12);
                core.ChronicleOption1.EquipmentType = ReadByte(data, offset + 14);
                core.ChronicleOption1.OptionNo = ReadByte(data, offset + 16);
            }
        }

        private static void ApplyTailData(ItemCore core, byte[] data, int offset = 0)
        {
            core.EmblemSocketCount = ReadByte(data, offset);
            core.EmblemId1 = ReadInt32(data, offset + 1);
            core.EmblemId2 = ReadInt32(data, offset + 5);
            core.Rune = ReadUInt16(data, offset + 9);
            core.RandomOption0.Type = ReadByte(data, offset + 12);
            core.RandomOption1.Type = ReadByte(data, offset + 13);
            core.RandomOption2.Type = ReadByte(data, offset + 14);
            core.RandomOption0.Value1 = ReadByte(data, offset + 15);
            core.RandomOption1.Value1 = ReadByte(data, offset + 16);
            core.RandomOption2.Value1 = ReadByte(data, offset + 17);
            core.RandomOption0.Value2 = ReadByte(data, offset + 18);
            core.RandomOption1.Value2 = ReadByte(data, offset + 19);
            core.RandomOption2.Value2 = ReadByte(data, offset + 20);
            core.RandomOptionState = ReadByte(data, offset + 21);
            core.RandomOptionChangedIndex = ReadByte(data, offset + 22, ItemCore.RandomOptionChangedIndexDefault);
            core.RandomOptionChangeState = ReadByte(data, offset + 23);
            core.RandomOptionChange.Type = ReadByte(data, offset + 24);
            core.RandomOptionChange.Value1 = ReadByte(data, offset + 25);
            core.RandomOptionChange.Value2 = ReadByte(data, offset + 26);
            core.GenuineUpgrade = ReadByte(data, offset + 27);
            core.EmancipateEquipmentLevel = ReadByte(data, offset + 28);
            core.TradeRestriction = ReadByte(data, offset + 29);
            core.TailUnknown0 = ReadUInt16(data, offset + 30);
            core.TailUnknown1 = ReadByte(data, offset + 32);
            core.TailUnknown2 = ReadByte(data, offset + 33);
            core.TailUnknown3 = ReadByte(data, offset + 34);
            core.RemainUseCount = ReadByte(data, offset + 35);
            core.SortLockFlag = ReadByte(data, offset + 36);
        }

        private static void ApplyChronicleOptions(ItemCore core, MakeEquipListCodec.ChronicleOptionFields[] fields)
        {
            if (fields == null)
                return;

            var options = new List<ChronicleOption>(Math.Min(fields.Length, 2));
            for (var index = 0; index < fields.Length && index < 2; index++)
            {
                options.Add(new ChronicleOption
                {
                    OptionId = fields[index].OptionId,
                    CharacJob = fields[index].CharacJob,
                    FirstGrowType = fields[index].FirstGrowType,
                    EquipmentType = fields[index].EquipmentType,
                    OptionNo = fields[index].OptionNo,
                });
            }

            core.SetChronicleOptions(options);
        }

        private static void ApplyRandomOptions(ItemCore core, MakeEquipListCodec.DisplayFields fields)
        {
            var options = new List<RandomOption>(3);
            for (var index = 0; index < 3; index++)
            {
                options.Add(new RandomOption
                {
                    Type = ReadArrayByte(fields.MagicSealTypes, index),
                    Value1 = ReadArrayByte(fields.MagicSealVal1s, index),
                    Value2 = ReadArrayByte(fields.MagicSealVal2s, index),
                });
            }

            core.SetRandomOptions(options);
        }

        private static long InsertNewItem(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long? itemUid,
            string ownerScope,
            int ownerId,
            int characterId,
            InventoryListType listType,
            short slotIndex,
            ItemCore core,
            string createdAt,
            string updatedAt)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = itemUid.HasValue
                    ? @"
INSERT OR REPLACE INTO character_new_items (
    item_uid, owner_scope, owner_id, character_id, list_type, slot_index, item_core, created_at, updated_at
) VALUES (
    @itemUid, @ownerScope, @ownerId, @characterId, @listType, @slotIndex, @itemCore,
    COALESCE(@createdAt, CURRENT_TIMESTAMP), COALESCE(@updatedAt, CURRENT_TIMESTAMP)
);"
                    : @"
INSERT INTO character_new_items (
    owner_scope, owner_id, character_id, list_type, slot_index, item_core, created_at, updated_at
) VALUES (
    @ownerScope, @ownerId, @characterId, @listType, @slotIndex, @itemCore,
    COALESCE(@createdAt, CURRENT_TIMESTAMP), COALESCE(@updatedAt, CURRENT_TIMESTAMP)
);";

                if (itemUid.HasValue)
                    command.Parameters.AddWithValue("@itemUid", itemUid.Value);

                command.Parameters.AddWithValue("@ownerScope", ownerScope);
                command.Parameters.AddWithValue("@ownerId", ownerId);
                command.Parameters.AddWithValue("@characterId", characterId == 0 ? (object)DBNull.Value : characterId);
                command.Parameters.AddWithValue("@listType", (int)listType);
                command.Parameters.AddWithValue("@slotIndex", slotIndex);
                command.Parameters.AddWithValue("@itemCore", core.ToBytes());
                command.Parameters.AddWithValue("@createdAt", string.IsNullOrWhiteSpace(createdAt) ? (object)DBNull.Value : createdAt);
                command.Parameters.AddWithValue("@updatedAt", string.IsNullOrWhiteSpace(updatedAt) ? (object)DBNull.Value : updatedAt);

                command.ExecuteNonQuery();
                if (itemUid.HasValue)
                    return itemUid.Value;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT last_insert_rowid();";
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private static void InsertAccountCargoNewItem(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long itemUid,
            int accountId,
            int characterId,
            InventoryListType listType,
            short slotIndex,
            ItemCore core,
            string createdAt,
            string updatedAt)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT OR REPLACE INTO account_cargo_new_items (
    item_uid, account_id, character_id, list_type, slot_index, item_core, created_at, updated_at
) VALUES (
    @itemUid, @accountId, @characterId, @listType, @slotIndex, @itemCore,
    COALESCE(@createdAt, CURRENT_TIMESTAMP), COALESCE(@updatedAt, CURRENT_TIMESTAMP)
);";
                command.Parameters.AddWithValue("@itemUid", itemUid);
                command.Parameters.AddWithValue("@accountId", accountId);
                command.Parameters.AddWithValue("@characterId", characterId == 0 ? (object)DBNull.Value : characterId);
                command.Parameters.AddWithValue("@listType", (int)listType);
                command.Parameters.AddWithValue("@slotIndex", slotIndex);
                command.Parameters.AddWithValue("@itemCore", core.ToBytes());
                command.Parameters.AddWithValue("@createdAt", string.IsNullOrWhiteSpace(createdAt) ? (object)DBNull.Value : createdAt);
                command.Parameters.AddWithValue("@updatedAt", string.IsNullOrWhiteSpace(updatedAt) ? (object)DBNull.Value : updatedAt);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertAvatarDetail(SqliteConnection connection, SqliteTransaction transaction, AvatarDetail detail)
        {
            var record = AvatarDetailCodec.ToRecord(detail);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT OR REPLACE INTO character_avatar_detail (
    item_uid, owner_id, character_id, item_id, expire_date, clear_avatar_id, jewel_socket, color1, color2, delete_date
) VALUES (
    @itemUid, @ownerId, @characterId, @itemId, @expireDate, @clearAvatarId, @jewelSocket, @color1, @color2, @deleteDate
);";
                command.Parameters.AddWithValue("@itemUid", record.AvatarUid);
                command.Parameters.AddWithValue("@ownerId", record.OwnerId);
                command.Parameters.AddWithValue("@characterId", record.CharacterId);
                command.Parameters.AddWithValue("@itemId", record.ItemId);
                command.Parameters.AddWithValue("@expireDate", record.ExpireDate);
                command.Parameters.AddWithValue("@clearAvatarId", record.ClearAvatarId);
                command.Parameters.AddWithValue("@jewelSocket", CopyFixed(record.JewelSocket, 30));
                command.Parameters.AddWithValue("@color1", record.Color1);
                command.Parameters.AddWithValue("@color2", record.Color2);
                command.Parameters.AddWithValue("@deleteDate", record.DeleteDate);
                command.ExecuteNonQuery();
            }
        }

        private static long AllocateAvatarUid(SqliteConnection connection, SqliteTransaction transaction)
        {
            var avatarUid = AvatarDetailRepository.AllocateAvatarUid(connection, transaction);
            if (avatarUid <= 0 || avatarUid > int.MaxValue)
                throw new InvalidOperationException("分配时装UID失败。");

            return avatarUid;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static int NormalizeMarker16(int value)
        {
            return value == 0 ? ItemCore.Marker16Default : value;
        }

        private static byte ReadArrayByte(byte[] data, int index)
        {
            return data != null && index >= 0 && index < data.Length ? data[index] : (byte)0;
        }

        private static byte ReadByte(byte[] data, int offset, byte defaultValue = 0)
        {
            return data != null && offset >= 0 && offset < data.Length ? data[offset] : defaultValue;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 1 >= data.Length)
                return 0;

            return BitConverter.ToUInt16(data, offset);
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 3 >= data.Length)
                return 0;

            return BitConverter.ToInt32(data, offset);
        }

        private static int ToInt32(long value)
        {
            if (value > int.MaxValue)
                return int.MaxValue;

            if (value < int.MinValue)
                return int.MinValue;

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static byte[] CopyFixed(byte[] data, int expectedLength)
        {
            var result = new byte[expectedLength];
            if (data == null || data.Length == 0)
                return result;

            Buffer.BlockCopy(data, 0, result, 0, Math.Min(data.Length, expectedLength));
            return result;
        }

        private sealed class LegacyExtraPayload
        {
            private LegacyExtraPayload(JsonObject json)
            {
                ExtData0 = ReadJsonByte(json, "extData0");
                PrefixData0E = ReadHexFixed(json, "prefixData0E", 8);
                MiddleData1A = ReadHexFixed(json, "middleData1A", 17);
                TailData2F = ReadHexFixed(json, "tailData2F", 37);
                AvatarReserved0 = ReadHexFixed(json, "reserved0", 5);
                AvatarReserved1 = ReadHexFixed(json, "reserved1", 71);
                AvatarSocketData = AvatarSocketDataCodec.Normalize(ReadHexFixed(json, "reserved2", 30));
                AvatarTailData = ReadHexFixed(json, "tailData", 7);
                PetTailData0A = ReadHexFixed(json, "tailData0A", 74);
            }

            public byte ExtData0 { get; }

            public byte[] PrefixData0E { get; }

            public byte[] MiddleData1A { get; }

            public byte[] TailData2F { get; }

            public byte[] AvatarReserved0 { get; }

            public byte[] AvatarReserved1 { get; }

            public byte[] AvatarSocketData { get; }

            public byte[] AvatarTailData { get; }

            public byte[] PetTailData0A { get; }

            public static LegacyExtraPayload Parse(string extraJson)
            {
                JsonObject json = null;
                if (!string.IsNullOrWhiteSpace(extraJson))
                {
                    try
                    {
                        json = JsonNode.Parse(extraJson) as JsonObject;
                    }
                    catch
                    {
                        json = null;
                    }
                }

                return new LegacyExtraPayload(json ?? new JsonObject());
            }

            private static byte ReadJsonByte(JsonObject json, string propertyName)
            {
                return Convert.ToByte(ReadJsonInt(json, propertyName) & 0xFF, CultureInfo.InvariantCulture);
            }

            private static int ReadJsonInt(JsonObject json, string propertyName)
            {
                if (!json.TryGetPropertyValue(propertyName, out var node) || node == null)
                    return 0;

                return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0;
            }

            private static byte[] ReadHexFixed(JsonObject json, string propertyName, int expectedLength)
            {
                return CopyFixed(ReadHexActual(json, propertyName), expectedLength);
            }

            private static byte[] ReadHexActual(JsonObject json, string propertyName)
            {
                if (!json.TryGetPropertyValue(propertyName, out var node) || node == null)
                    return Array.Empty<byte>();

                var hex = node.ToString();
                if (string.IsNullOrWhiteSpace(hex))
                    return Array.Empty<byte>();

                var data = new byte[hex.Length / 2];
                for (var index = 0; index < data.Length; index++)
                {
                    if (!byte.TryParse(hex.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data[index]))
                        return Array.Empty<byte>();
                }

                return data;
            }
        }

        internal sealed class CharacterItemRow
        {
            public long ItemUid { get; set; }
            public string OwnerScope { get; set; }

            public int OwnerId { get; set; }

            public int CharacterId { get; set; }

            public InventoryListType ListType { get; set; }

            public short SlotIndex { get; set; }

            public int ItemTemplateId { get; set; }

            public string ItemKindText { get; set; }

            public int StackCount { get; set; }

            public int InstanceValue { get; set; }

            public ushort Durability { get; set; }

            public byte SealFlag { get; set; }

            public byte OptionValue { get; set; }

            public byte EquipmentLockId { get; set; }

            public int ExpireTime { get; set; }

            public int Marker16 { get; set; }

            public int PetSerialOrHandle { get; set; }

            public string ExtraJson { get; set; }

            public string CreatedAt { get; set; }

            public string UpdatedAt { get; set; }
        }

        private sealed class MainVirtualCurrencyRow
        {
            public int CharacterId { get; set; }

            public short SlotIndex { get; set; }

            public int Count { get; set; }
        }

        internal sealed class AccountCargoItemRow
        {
            public long ItemUid { get; set; }

            public int AccountId { get; set; }

            public int CharacterId { get; set; }

            public short SlotIndex { get; set; }

            public int ItemTemplateId { get; set; }

            public string ItemKindText { get; set; }

            public int StackCount { get; set; }

            public int InstanceValue { get; set; }

            public ushort Durability { get; set; }

            public byte SealFlag { get; set; }

            public byte OptionValue { get; set; }

            public int ExpireTime { get; set; }

            public int Marker16 { get; set; }

            public string ExtraJson { get; set; }

            public string CreatedAt { get; set; }

            public string UpdatedAt { get; set; }
        }

        internal sealed class EquippedEntryRow
        {
            public int CharacterId { get; set; }

            public short SlotIndex { get; set; }

            public int ItemTemplateId { get; set; }

            public int ExpireTime { get; set; }

            public byte EquipmentLockId { get; set; }

            public byte[] RawEntry { get; set; }
        }

    }
}
