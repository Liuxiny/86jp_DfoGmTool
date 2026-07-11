// GM瘦身拷贝: 相对服务端原版删除了 EquipmentLockError* 常量, TryLockEquipmentItem, TryUnlockEquipmentItem,
// TryCancelEquipmentItemUnlock, LoadEquipmentItemLocks(全部重载), LoadEquipmentLockTarget,
// LoadEquippedEquipmentLockTarget, TryValidateEquipmentLockTarget, UpdateTargetEquipmentLockId,
// AllocateEquipmentLockId, UpsertEquipmentLock, TryLoadEquipmentLockState, DeleteEquipmentLock,
// IsSupportedEquipmentLockListType, CreateEquipmentLockResult, IsEquipmentType,
// IsEquipmentLockTradeDeleteAttachType, NormalizeEquipmentLockPvfToken, EquipmentLockTarget 类;
// 保留成员与原版逐字一致
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed partial class SqliteInventoryStore
    {
        internal static bool IsEquipmentItemLocked(SqliteConnection connection, SqliteTransaction transaction, int characterId, ItemRecord target)
        {
            return target != null && IsEquipmentLockIdActive(connection, transaction, characterId, target.EquipmentLockId);
        }

        internal static bool IsEquipmentLockIdActive(SqliteConnection connection, SqliteTransaction transaction, int characterId, byte equipmentLockId)
        {
            if (equipmentLockId == 0)
                return false;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
SELECT COUNT(1)
FROM character_item_locks
WHERE character_id = @cid
  AND state != 0
  AND equipment_lock_id = @lockId;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@lockId", (int)equipmentLockId);
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }
    }
}
