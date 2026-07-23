using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DfoGmTool.ServerCore.Game.Inventory;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        private int StripJobRestrictedEquippedItems(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            HashSet<string> selected)
        {
            if (!selected.Contains("equipped") || !TableExists(conn, tx, "character_equipped_entries"))
                return 0;

            var itemIndex = (_pvfIndex.AllItems ?? Array.Empty<PvfIndexService.ItemEntry>())
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var restricted = new List<RestrictedEquippedRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT slot, item_id, raw_entry, expire_time, equipment_lock_id
FROM character_equipped_entries
WHERE character_id = @cid
ORDER BY slot;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var itemId = reader.GetInt32(1);
                        if (!itemIndex.TryGetValue(itemId, out var item) || !HasExplicitJobRestriction(item.UsableJob))
                            continue;
                        restricted.Add(new RestrictedEquippedRow(
                            Convert.ToInt16(reader.GetInt32(0), CultureInfo.InvariantCulture),
                            itemId,
                            reader.IsDBNull(2) ? Array.Empty<byte>() : (byte[])reader.GetValue(2),
                            reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                            reader.IsDBNull(4) ? (byte)0 : Convert.ToByte(reader.GetInt32(4), CultureInfo.InvariantCulture)));
                    }
                }
            }

            foreach (var row in restricted)
            {
                var destination = _store._equipStore.RestoreEquippedEntryToContainer(
                    conn, tx, characterId, row.Slot, row.ItemId, row.Raw, row.ExpireTime, row.EquipmentLockId);

                using (var delete = conn.CreateCommand())
                {
                    delete.Transaction = tx;
                    delete.CommandText = "DELETE FROM character_equipped_entries WHERE character_id = @cid AND slot = @slot;";
                    delete.Parameters.AddWithValue("@cid", characterId);
                    delete.Parameters.AddWithValue("@slot", row.Slot);
                    if (delete.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException($"脱下职业限制装备失败: itemId={row.ItemId} slot={row.Slot}");
                }

                if (row.EquipmentLockId > 0 && TableExists(conn, tx, "character_item_locks"))
                {
                    using (var updateLock = conn.CreateCommand())
                    {
                        updateLock.Transaction = tx;
                        updateLock.CommandText = @"
UPDATE character_item_locks
SET inventory_list_type = @listType, slot = @slot
WHERE character_id = @cid AND equipment_lock_id = @lockId;";
                        updateLock.Parameters.AddWithValue("@listType", (int)destination.ListType);
                        updateLock.Parameters.AddWithValue("@slot", destination.Slot);
                        updateLock.Parameters.AddWithValue("@cid", characterId);
                        updateLock.Parameters.AddWithValue("@lockId", row.EquipmentLockId);
                        updateLock.ExecuteNonQuery();
                    }
                }
            }

            if (restricted.Count > 0 && ColumnExists(conn, tx, "characters", "appearance_blob"))
            {
                using (var clearAppearance = conn.CreateCommand())
                {
                    clearAppearance.Transaction = tx;
                    clearAppearance.CommandText = "UPDATE characters SET appearance_blob = NULL, updated_at = CURRENT_TIMESTAMP WHERE character_id = @cid;";
                    clearAppearance.Parameters.AddWithValue("@cid", characterId);
                    clearAppearance.ExecuteNonQuery();
                }
            }

            var restrictedIds = new HashSet<int>(itemIndex.Values
                .Where(item => HasExplicitJobRestriction(item.UsableJob))
                .Select(item => item.Id));
            if (restrictedIds.Count > 0)
            {
                using (var verify = conn.CreateCommand())
                {
                    verify.Transaction = tx;
                    verify.CommandText = "SELECT item_id FROM character_equipped_entries WHERE character_id = @cid;";
                    verify.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = verify.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (restrictedIds.Contains(reader.GetInt32(0)))
                                throw new InvalidOperationException("复制后仍存在职业限制穿戴项，已回滚");
                        }
                    }
                }
            }
            return restricted.Count;
        }

        private static bool HasExplicitJobRestriction(string usableJob)
        {
            if (string.IsNullOrWhiteSpace(usableJob))
                return false;
            var normalized = usableJob.Replace("`", string.Empty).Trim().ToLowerInvariant();
            return normalized.Length > 0
                && !normalized.Equals("[all]", StringComparison.Ordinal)
                && !normalized.Equals("all", StringComparison.Ordinal);
        }

        private sealed class RestrictedEquippedRow
        {
            public RestrictedEquippedRow(short slot, int itemId, byte[] raw, int expireTime, byte equipmentLockId)
            {
                Slot = slot;
                ItemId = itemId;
                Raw = raw ?? Array.Empty<byte>();
                ExpireTime = expireTime;
                EquipmentLockId = equipmentLockId;
            }

            public short Slot { get; }
            public int ItemId { get; }
            public byte[] Raw { get; }
            public int ExpireTime { get; }
            public byte EquipmentLockId { get; }
        }
    }
}
