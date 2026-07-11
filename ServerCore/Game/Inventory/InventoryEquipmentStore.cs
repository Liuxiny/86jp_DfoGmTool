// GM瘦身拷贝: 相对服务端原版仅保留 构造器/LoadContainerState/LoadAccountCargoState
// (快照读路径所需); 删除了穿脱装备/租赁武器/名称装饰卡/容器写入/装备条目编解码等全部其余成员;
// 保留成员与原版逐字一致
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class InventoryEquipmentStore
    {
        private readonly InventoryDbPrimitives _db;
        private readonly InventoryAuditLogger _auditLogger;

        internal InventoryEquipmentStore(InventoryDbPrimitives db, InventoryAuditLogger auditLogger)
        {
            _db = db;
            _auditLogger = auditLogger;
        }

        internal Dictionary<InventoryListType, ushort> LoadContainerState(SqliteConnection connection, SqliteTransaction transaction, int characterId, int accountId)
        {
            var states = new Dictionary<InventoryListType, ushort>();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT list_type, list_param16
FROM character_container_state
WHERE character_id = @characterId;";
                command.Parameters.AddWithValue("@characterId", characterId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        states[(InventoryListType)reader.GetInt32(0)] = Convert.ToUInt16(reader.GetInt32(1), CultureInfo.InvariantCulture);
                }
            }

            return states;
        }

        internal AccountCargoStateSnapshot LoadAccountCargoState(SqliteConnection connection, SqliteTransaction transaction, int characterId, int accountId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT selection_key, value32, item_count
FROM account_cargo_state
WHERE account_id = @accountId;";
                command.Parameters.AddWithValue("@accountId", accountId);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return new AccountCargoStateSnapshot();

                    return new AccountCargoStateSnapshot
                    {
                        SelectionKey = Convert.ToUInt16(reader.GetInt32(0), CultureInfo.InvariantCulture),
                        Value32 = reader.GetInt32(1),
                        ItemCount = Convert.ToUInt16(reader.GetInt32(2), CultureInfo.InvariantCulture),
                    };
                }
            }
        }
    }
}
