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

        // 复制角色安全处理使用服务端穿脱装备的同一数据库表示：先找空槽，再把 equipped raw
        // 还原为 character_items。调用方负责在同一事务内删除原 equipped 行。
        internal (InventoryListType ListType, short Slot) RestoreEquippedEntryToContainer(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            short equippedSlot,
            int itemId,
            byte[] entryRaw,
            int entryExpireTime,
            byte equipmentLockId)
        {
            if (!ItemSlotBoundService.TryResolveItemKindForMigration(
                    InventoryListType.Equipment, equippedSlot, itemId, out var itemKind))
                throw new InvalidOperationException($"穿戴槽位无法映射到背包 itemId={itemId} equipSlot={equippedSlot}");
            if (!NewInventoryStore.TryFindFirstFreeLegacyCharacterBagSlot(
                    connection, transaction, characterId, itemKind,
                    out var listType, out _, out var targetSlot, out var destinationError))
                throw new InvalidOperationException($"背包没有空位，无法脱下穿戴物 itemId={itemId} equipSlot={equippedSlot}: {destinationError}");

            if (ItemMetadataResolver.IsCloneAvatarItem(itemId) && entryRaw != null && entryRaw.Length >= 16)
                Array.Clear(entryRaw, 12, 4);

            var fields = entryRaw != null && entryRaw.Length >= 24
                ? MakeEquipListCodec.ParseDisplayFields(entryRaw)
                : new MakeEquipListCodec.DisplayFields();

            if (listType == InventoryListType.Avatar)
            {
                _db.InsertCharacterItem(
                    connection, transaction, characterId, listType, (short)targetSlot, itemId, "avatar",
                    stackCount: 0, instanceValue: 0, durability: 0, sealFlag: 0,
                    optionValue: fields.Durability != 0 ? unchecked((byte)(fields.Durability & 0xFF)) : fields.Reinforce,
                    expireTime: 0, marker16: SqliteInventoryStore.DefaultAvatarUnknownFixed30,
                    petSerialOrHandle: 0, extraJson: SqliteInventoryStore.CreateDefaultAvatarExtraJson(),
                    equipmentLockId: equipmentLockId);
            }
            else if (listType == InventoryListType.Pet)
            {
                var petHandle = entryRaw != null && entryRaw.Length >= 9 ? BitConverter.ToInt32(entryRaw, 5) : 0;
                _db.InsertCharacterItem(
                    connection, transaction, characterId, listType, (short)targetSlot, itemId, "pet",
                    stackCount: 0, instanceValue: 0, durability: 0, sealFlag: 0, optionValue: 0,
                    expireTime: 0, marker16: 0, petSerialOrHandle: petHandle,
                    extraJson: "{}", equipmentLockId: equipmentLockId);
            }
            else
            {
                var builder = new ItemExtraViewBuilder();
                builder.Equipment.Upgrade = fields.Reinforce;
                builder.Equipment.EnchantCardId = unchecked((int)fields.Enchant);
                builder.Equipment.EnchantUpgradeCount = fields.EnchantUpgradeCount;
                builder.Equipment.AmplifyType = fields.AmplifyType;
                builder.Equipment.AmplifyValue = fields.AmplifyValue;
                builder.Equipment.EmblemData = fields.Emblem;
                builder.Equipment.Rune = fields.Rune;
                builder.Equipment.SealCount = fields.MagicSealCount;
                builder.Equipment.SealTypes = fields.MagicSealTypes;
                builder.Equipment.SealVal1s = fields.MagicSealVal1s;
                builder.Equipment.SealVal2s = fields.MagicSealVal2s;
                builder.Equipment.SealTail = fields.MagicSealTail;
                builder.Equipment.Forging = fields.Forging;
                builder.Equipment.JewelSocket = fields.JewelSocket;
                _db.InsertCharacterItem(
                    connection, transaction, characterId, listType, (short)targetSlot, itemId, "equipment",
                    stackCount: fields.InstanceValue != 0 ? unchecked((int)fields.InstanceValue) : itemId, instanceValue: 0,
                    durability: fields.Durability, sealFlag: 0, optionValue: 0,
                    expireTime: entryExpireTime, marker16: -1, petSerialOrHandle: 0,
                    extraJson: builder.Build().Serialize(), equipmentLockId: equipmentLockId);
            }

            return (listType, targetSlot);
        }
    }
}
