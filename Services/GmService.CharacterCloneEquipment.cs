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
            if (!selected.Contains("equipped") || !TableExists(conn, tx, "character_new_items"))
                return 0;

            var itemIndex = (_pvfIndex.AllItems ?? Array.Empty<PvfIndexService.ItemEntry>())
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var restricted = new List<RestrictedEquippedRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT item_uid, slot_index, item_core
FROM character_new_items
WHERE character_id = @cid AND list_type = 3
ORDER BY slot_index;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var coreBytes = reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2);
                        if (coreBytes == null || coreBytes.Length != ItemCore.Size)
                            throw new InvalidOperationException("复制后的穿戴 ItemCore 长度无效");
                        var core = ItemCore.FromBytes(coreBytes);
                        var itemId = core.ItemId;
                        if (!itemIndex.TryGetValue(itemId, out var item) || !HasExplicitJobRestriction(item.UsableJob))
                            continue;
                        restricted.Add(new RestrictedEquippedRow(
                            reader.GetInt64(0),
                            Convert.ToInt16(reader.GetInt32(1), CultureInfo.InvariantCulture),
                            itemId,
                            core.EquipmentLockId));
                    }
                }
            }

            foreach (var row in restricted)
            {
                var destinationSlot = FindFreeCloneMainEquipmentSlot(conn, tx, characterId);
                if (destinationSlot < 0)
                    throw new InvalidOperationException($"复制后职业限制装备无法脱下：装备背包已满 itemId={row.ItemId}");
                using (var move = conn.CreateCommand())
                {
                    move.Transaction = tx;
                    move.CommandText = @"UPDATE character_new_items
SET list_type=0,slot_index=@target,updated_at=CURRENT_TIMESTAMP
WHERE item_uid=@uid AND character_id=@cid AND list_type=3;";
                    move.Parameters.AddWithValue("@target", destinationSlot);
                    move.Parameters.AddWithValue("@uid", row.ItemUid);
                    move.Parameters.AddWithValue("@cid", characterId);
                    try
                    {
                        if (move.ExecuteNonQuery() != 1)
                            throw new InvalidOperationException($"脱下职业限制装备失败: itemId={row.ItemId} slot={row.Slot}");
                    }
                    catch (SqliteException ex)
                    {
                        throw new InvalidOperationException($"脱下职业限制装备发生目标槽冲突: itemId={row.ItemId} from={row.Slot} to={destinationSlot}", ex);
                    }
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
                        updateLock.Parameters.AddWithValue("@listType", (int)InventoryListType.Main);
                        updateLock.Parameters.AddWithValue("@slot", destinationSlot);
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
                    verify.CommandText = "SELECT item_core FROM character_new_items WHERE character_id = @cid AND list_type=3;";
                    verify.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = verify.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var bytes = reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0);
                            if (bytes != null && bytes.Length == ItemCore.Size && restrictedIds.Contains(ItemCore.FromBytes(bytes).ItemId))
                                throw new InvalidOperationException("复制后仍存在职业限制穿戴项，已回滚");
                        }
                    }
                }
            }
            return restricted.Count;
        }

        private static short FindFreeCloneMainEquipmentSlot(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            var occupied = new HashSet<int>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT slot_index FROM character_new_items
WHERE character_id=@cid AND list_type=0 AND slot_index BETWEEN 9 AND 64;";
                command.Parameters.AddWithValue("@cid", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    occupied.Add(reader.GetInt32(0));
            }
            for (short slot = 9; slot <= 64; slot++)
                if (!occupied.Contains(slot))
                    return slot;
            return -1;
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
            public RestrictedEquippedRow(long itemUid, short slot, int itemId, byte equipmentLockId)
            {
                ItemUid = itemUid;
                Slot = slot;
                ItemId = itemId;
                EquipmentLockId = equipmentLockId;
            }

            public long ItemUid { get; }
            public short Slot { get; }
            public int ItemId { get; }
            public byte EquipmentLockId { get; }
        }
    }
}
