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
            if (!selected.Contains("equipped") || !TableExists(conn, tx, "character_inventory_items"))
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
FROM character_inventory_items
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
                        if (!itemIndex.TryGetValue(itemId, out var item)
                            || !HasExplicitJobRestriction(item.UsableJob))
                            continue;
                        restricted.Add(new RestrictedEquippedRow(
                            reader.GetInt64(0),
                            Convert.ToInt16(reader.GetInt32(1), CultureInfo.InvariantCulture),
                            itemId,
                            core.ItemKind,
                            core.EquipmentLockId));
                    }
                }
            }

            foreach (var row in restricted)
            {
                if (!NewInventoryStore.TryFindFirstFreeCharacterBagSlot(
                        conn,
                        tx,
                        characterId,
                        row.ItemKind,
                        out var destinationList,
                        out _,
                        out var destinationSlot,
                        out var destinationError))
                    throw new InvalidOperationException($"复制后职业限制穿戴物无法脱下：{destinationError} itemId={row.ItemId} kind={row.ItemKind}");
                using (var move = conn.CreateCommand())
                {
                    move.Transaction = tx;
                    move.CommandText = @"UPDATE character_inventory_items
SET list_type=@targetList,slot_index=@target,updated_at=CURRENT_TIMESTAMP
WHERE item_uid=@uid AND character_id=@cid AND list_type=3;";
                    move.Parameters.AddWithValue("@targetList", (int)destinationList);
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
                        throw new InvalidOperationException($"脱下职业限制穿戴物发生目标槽冲突: itemId={row.ItemId} from={row.Slot} to={destinationList}:{destinationSlot}", ex);
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
                        updateLock.Parameters.AddWithValue("@listType", (int)destinationList);
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
                    verify.CommandText = "SELECT item_core FROM character_inventory_items WHERE character_id = @cid AND list_type=3;";
                    verify.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = verify.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var bytes = reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0);
                            if (bytes != null && bytes.Length == ItemCore.Size)
                            {
                                var core = ItemCore.FromBytes(bytes);
                                if (restrictedIds.Contains(core.ItemId))
                                    throw new InvalidOperationException("复制后仍存在带职业限制的穿戴物，已回滚");
                            }
                        }
                    }
                }
            }
            return restricted.Count;
        }

        private static void ValidateClonedInventoryLayout(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId)
        {
            var mainExpandStage = LoadCloneContainerListParam(connection, transaction, characterId, 0, 24);
            var personalCargoCapacity = LoadCloneContainerListParam(connection, transaction, characterId, 2, 8);
            personalCargoCapacity = personalCargoCapacity <= 0 ? 8 : Math.Min(personalCargoCapacity, 152);
            var exEquipSlotStat = LoadCloneExtraEquipmentSlotStat(connection, transaction, characterId);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"SELECT list_type,slot_index,item_core
FROM character_inventory_items
WHERE character_id=@cid
ORDER BY list_type,slot_index;";
            command.Parameters.AddWithValue("@cid", characterId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var listTypeValue = reader.GetInt32(0);
                var slot = Convert.ToInt16(reader.GetInt32(1), CultureInfo.InvariantCulture);
                var bytes = reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2);
                if (bytes == null || bytes.Length != ItemCore.Size)
                    throw new InvalidOperationException($"复制后的 ItemCore 长度无效: list={listTypeValue} slot={slot}");
                var core = ItemCore.FromBytes(bytes);
                if (listTypeValue == (int)InventoryListType.Main && slot >= 0 && slot <= 2)
                    continue;
                if (!IsCloneSlotOpen(listTypeValue, slot, mainExpandStage, personalCargoCapacity, exEquipSlotStat))
                    throw new InvalidOperationException($"复制后的物品位未开放或已保留: list={listTypeValue} slot={slot} itemId={core.ItemId}");
                if ((listTypeValue == (int)InventoryListType.Main && slot >= 3 && slot <= 8)
                    || listTypeValue == (int)InventoryListType.PersonalCargo)
                    continue;
                if (!TryResolveA21SlotKind((InventoryListType)listTypeValue, slot, out var expectedKind)
                    || expectedKind != core.ItemKind)
                    throw new InvalidOperationException($"复制后的物品容器不匹配: list={listTypeValue} slot={slot} kind={core.ItemKind} expected={expectedKind} itemId={core.ItemId}");
            }
        }

        private static bool TryResolveA21SlotKind(InventoryListType listType, short slot, out byte kind)
        {
            kind = ItemCore.KindUnknown;
            switch (listType)
            {
                case InventoryListType.Main:
                    if (slot >= 9 && slot <= 64) kind = ItemCore.KindEquipment;
                    else if (slot >= 65 && slot <= 120) kind = ItemCore.KindConsumable;
                    else if (slot >= 121 && slot <= 176) kind = ItemCore.KindMaterial;
                    else if (slot >= 177 && slot <= 232) kind = ItemCore.KindQuest;
                    else if (slot >= 233 && slot <= 288) kind = ItemCore.KindExpertJobMaterial;
                    else if (slot >= 289 && slot <= 351) kind = ItemCore.KindAvatarEmblem;
                    else return false;
                    return true;
                case InventoryListType.Avatar:
                    kind = ItemCore.KindAvatar;
                    return slot >= 0 && slot <= 209;
                case InventoryListType.Pet:
                    if (slot >= 0 && slot <= 139) kind = ItemCore.KindCreature;
                    else if (slot >= 140 && slot <= 188) kind = ItemCore.KindCreatureEquipment;
                    else if (slot >= 189 && slot <= 239) kind = ItemCore.KindCreatureConsumable;
                    else return false;
                    return true;
                case InventoryListType.GuildMedal:
                    if (slot >= 0 && slot <= 48) kind = ItemCore.KindGuildMedal;
                    else if (slot >= 49 && slot <= 97) kind = ItemCore.KindGuardianGem;
                    else return false;
                    return true;
                case InventoryListType.Equipment:
                    if (slot >= 0 && slot <= 10) kind = ItemCore.KindAvatar;
                    else if ((slot >= 11 && slot <= 23) || slot == 29) kind = ItemCore.KindEquipment;
                    else if (slot == 24) kind = ItemCore.KindCreature;
                    else if (slot >= 25 && slot <= 27) kind = ItemCore.KindCreatureEquipment;
                    else return false;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsCloneSlotOpen(
            int listType,
            short slot,
            int mainExpandStage,
            int personalCargoCapacity,
            int exEquipSlotStat)
        {
            if (listType == (int)InventoryListType.Main)
            {
                if (slot >= 3 && slot <= 8)
                    return true;
                if (slot >= 9 && slot <= GetExpandedCloneMainEnd(64, mainExpandStage))
                    return true;
                if (slot >= 65 && slot <= GetExpandedCloneMainEnd(120, mainExpandStage))
                    return true;
                if (slot >= 121 && slot <= GetExpandedCloneMainEnd(176, mainExpandStage))
                    return true;
                if (slot >= 177 && slot <= GetExpandedCloneMainEnd(232, mainExpandStage))
                    return true;
                if (slot >= 233 && slot <= GetExpandedCloneMainEnd(288, mainExpandStage))
                    return true;
                return slot >= 289 && slot <= 351;
            }

            if (listType == (int)InventoryListType.Avatar)
                return slot >= 0 && slot <= 209;
            if (listType == (int)InventoryListType.PersonalCargo)
                return slot >= 0 && slot < personalCargoCapacity;
            if (listType == (int)InventoryListType.Pet)
                return slot >= 0 && slot <= 239;
            if (listType == (int)InventoryListType.GuildMedal)
                return slot >= 0 && slot <= 97;
            if (listType != (int)InventoryListType.Equipment)
                return false;

            if (slot >= 0 && slot <= 20)
                return true;
            if (slot >= 21 && slot <= 23)
                return (exEquipSlotStat & (1 << (slot - 21))) != 0;
            return (slot >= 24 && slot <= 27) || slot == 29;
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
            public RestrictedEquippedRow(long itemUid, short slot, int itemId, byte itemKind, byte equipmentLockId)
            {
                ItemUid = itemUid;
                Slot = slot;
                ItemId = itemId;
                ItemKind = itemKind;
                EquipmentLockId = equipmentLockId;
            }

            public long ItemUid { get; }
            public short Slot { get; }
            public int ItemId { get; }
            public byte ItemKind { get; }
            public byte EquipmentLockId { get; }
        }
    }
}
