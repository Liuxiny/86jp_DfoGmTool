// GM瘦身拷贝: 相对服务端原版仅保留 快照读路径所需成员(RepairPetCreatureItemListSlotConflict,
// LoadPetCreatureVisibleEquipSlotRecord, FindEmptyVisiblePetCreatureSlotExceptEquipSlot,
// IsPersistentPetCreatureSerial, LoadPetCreatureEquippedEntry, LoadPetEquippedEntry,
// PetCreatureEquippedEntry 结构体, EnsureCreatureListEntry, CreatureDefaults 结构体, 相关常量);
// 删除了宠物穿脱/神器/序列号分配/遗留仓储修复/运行时状态等全部其余成员; 保留成员与原版逐字一致
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.SelectCharacter;
using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;
using GmPvfLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed partial class SqliteInventoryStore
    {
        private const short PetCreatureEquipSlot = 24;
        private const short PetCreatureEquippedStorageSlot = PetCreatureEquipSlot + 216;
        private const int MaxPersistentPetCreatureSerial = 0x000FFFFF;
        private const short PetArtifactRedEquipSlot = 25;
        private const short PetArtifactBlueEquipSlot = 26;
        private const short PetArtifactGreenEquipSlot = 27;
        private const int MinCreatureExpireUnixTime = 946684800; // 2000-01-01; filters out pet serials stored in CreatureExtra.
        private const byte DefaultCreatureField1 = 133;
        private const byte DefaultCreatureField2 = 47;
        private const byte DefaultCreatureField3 = 143;
        private const byte DefaultCreatureField4 = 105;

        private void RepairPetCreatureItemListSlotConflict(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            var equippedEntry = LoadPetCreatureEquippedEntry(connection, transaction, characterId);
            if (!equippedEntry.HasValue
                || !IsCreatureItem(equippedEntry.Value.ItemId)
                || !IsPersistentPetCreatureSerial(equippedEntry.Value.Serial))
                return;

            var item = LoadPetCreatureVisibleEquipSlotRecord(connection, transaction, characterId);
            if (item == null || !IsCreatureItem(item.ItemTemplateId))
                return;

            if (item.PetSerialOrHandle == equippedEntry.Value.Serial)
            {
                _db.DeleteItem(connection, transaction, item.ItemUid);
                FileLogger.Log($"  [PetCreatureMove] repair: removed stale pet item-list slot24 duplicate uid={item.ItemUid} item=0x{item.ItemTemplateId:X8} serial=0x{item.PetSerialOrHandle:X8}");
                return;
            }

            var targetSlot = FindEmptyVisiblePetCreatureSlotExceptEquipSlot(connection, transaction, characterId);
            if (targetSlot < 0)
            {
                FileLogger.Log($"  [PetCreatureMove] repair: pet item-list slot24 conflict kept, no empty visible slot uid={item.ItemUid} item=0x{item.ItemTemplateId:X8} serial=0x{item.PetSerialOrHandle:X8}");
                return;
            }

            _db.UpdateItemPosition(connection, transaction, item.ItemUid, InventoryListType.Pet, (short)targetSlot);
            FileLogger.Log($"  [PetCreatureMove] repair: moved pet item-list slot24 conflict uid={item.ItemUid} item=0x{item.ItemTemplateId:X8} serial=0x{item.PetSerialOrHandle:X8} -> slot {targetSlot}");
        }

        private ItemRecord LoadPetCreatureVisibleEquipSlotRecord(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
SELECT item_uid, list_type, slot_index, item_template_id, item_kind, stack_count, instance_value,
       durability, seal_flag, option_value, expire_time, marker_16, pet_serial_or_handle, extra_json
FROM character_items
WHERE character_id = @cid
  AND list_type = @lt
  AND slot_index = @slot
  AND item_kind = 'pet'
ORDER BY item_uid
LIMIT 1;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@lt", (int)InventoryListType.Pet);
                cmd.Parameters.AddWithValue("@slot", (int)PetCreatureEquipSlot);
                using (var reader = cmd.ExecuteReader())
                {
                    return reader.Read() ? ReadItemRecord(reader) : null;
                }
            }
        }

        private static int FindEmptyVisiblePetCreatureSlotExceptEquipSlot(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
WITH RECURSIVE slots(slot_index) AS (
    SELECT @start
    UNION ALL
    SELECT slot_index + 1 FROM slots WHERE slot_index < @end
)
SELECT slot_index
FROM slots
WHERE slot_index <> @excluded
  AND NOT EXISTS (
      SELECT 1
      FROM character_items
      WHERE character_id = @cid
        AND list_type = @lt
        AND slot_index = slots.slot_index
  )
ORDER BY slot_index
LIMIT 1;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@lt", (int)InventoryListType.Pet);
                cmd.Parameters.AddWithValue("@start", PetInventorySlotStart);
                cmd.Parameters.AddWithValue("@end", PetInventorySlotEnd);
                cmd.Parameters.AddWithValue("@excluded", (int)PetCreatureEquipSlot);
                var value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? -1
                    : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
        }

        private static bool IsPersistentPetCreatureSerial(int value)
        {
            return value > 0 && value <= MaxPersistentPetCreatureSerial;
        }

        private static PetCreatureEquippedEntry? LoadPetCreatureEquippedEntry(SqliteConnection connection, SqliteTransaction transaction, int characterId)
            => LoadPetEquippedEntry(connection, transaction, characterId, PetCreatureEquipSlot);

        private static PetCreatureEquippedEntry? LoadPetEquippedEntry(SqliteConnection connection, SqliteTransaction transaction, int characterId, int slot)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
SELECT item_id, raw_entry, expire_time
FROM character_equipped_entries
WHERE character_id = @cid AND slot = @slot
LIMIT 1;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@slot", slot);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    var itemId = reader.GetInt32(0);
                    var raw = reader.IsDBNull(1) ? null : (byte[])reader.GetValue(1);
                    var expireTime = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    if (raw == null)
                        return new PetCreatureEquippedEntry(itemId, 0, expireTime, 0);

                    try
                    {
                        var parsed = InvenItem.Parse(raw);
                        return new PetCreatureEquippedEntry(
                            parsed.ItemId,
                            unchecked((int)parsed.Value),
                            expireTime,
                            unchecked((int)parsed.CreatureExtra));
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Log($"  [PetCreatureMove] equip raw parse failed slot={slot} item=0x{itemId:X8}: {ex.Message}");
                        var serial = raw.Length >= 9 ? BitConverter.ToInt32(raw, 5) : 0;
                        return new PetCreatureEquippedEntry(itemId, serial, expireTime, 0);
                    }
                }
            }
        }

        private readonly struct PetCreatureEquippedEntry
        {
            public PetCreatureEquippedEntry(int itemId, int serial, int expireTime, int creatureExtra)
            {
                ItemId = itemId;
                Serial = serial;
                ExpireTime = expireTime;
                CreatureExtra = creatureExtra;
            }

            public int ItemId { get; }

            public int Serial { get; }

            public int ExpireTime { get; }

            public int CreatureExtra { get; }
        }

        private static void EnsureCreatureListEntry(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int petHandle,
            CreatureDefaults defaults)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
SELECT COUNT(1)
FROM character_creatures
WHERE character_id = @cid AND creature_key = @key;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@key", petHandle);
                if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    return;
            }

            int sortOrder;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
SELECT COALESCE(MAX(sort_order), -1) + 1
FROM character_creatures
WHERE character_id = @cid;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                sortOrder = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT INTO character_creatures(
    character_id, sort_order, creature_key, field04, mode_flag, progress_value,
    mode1_field0a, mode1_field0b, field_after_value, creature_text, tail_flag)
VALUES(
    @cid, @ord, @key, 100, 0, 0,
    0, 0, @level, @text, 0);";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@ord", sortOrder);
                cmd.Parameters.AddWithValue("@key", petHandle);
                cmd.Parameters.AddWithValue("@level", defaults.Level);
                cmd.Parameters.AddWithValue("@text", DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private readonly struct CreatureDefaults
        {
            public CreatureDefaults(int level, byte[] nameBytes)
            {
                Level = Math.Max(1, Math.Min(255, level));
                NameBytes = nameBytes ?? Array.Empty<byte>();
            }

            public int Level { get; }

            public byte[] NameBytes { get; }
        }
    }
}
