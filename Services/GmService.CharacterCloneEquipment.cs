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
            var mainExpandStage = LoadCloneContainerListParam(
                connection,
                transaction,
                characterId,
                (int)InventoryListType.Main,
                A21InventorySlotPolicy.MainExpandStageFull);
            if (!A21InventorySlotPolicy.TryNormalizeMainExpandStage(mainExpandStage, out mainExpandStage))
                throw new InvalidOperationException($"复制后的主背包扩展状态无效: {mainExpandStage}");
            var personalCargoCapacity = A21InventorySlotPolicy.NormalizePersonalCapacity(
                LoadCloneContainerListParam(
                    connection,
                    transaction,
                    characterId,
                    (int)InventoryListType.PersonalCargo,
                    A21InventorySlotPolicy.PersonalCargoDefaultCapacity));
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
                if (!IsCloneSlotOpen(listTypeValue, slot, mainExpandStage, personalCargoCapacity))
                    throw new InvalidOperationException($"复制后的物品位未开放或已保留: list={listTypeValue} slot={slot} itemId={core.ItemId}");
                if ((listTypeValue == (int)InventoryListType.Main && slot >= 3 && slot <= 8)
                    || listTypeValue == (int)InventoryListType.PersonalCargo)
                    continue;
                if (!A21InventorySlotPolicy.IsValidSlotForKind(
                        core.ItemKind,
                        (InventoryListType)listTypeValue,
                        slot,
                        mainExpandStage))
                    throw new InvalidOperationException($"复制后的物品容器不匹配: list={listTypeValue} slot={slot} kind={core.ItemKind} itemId={core.ItemId}");
            }
        }

        private static bool IsCloneSlotOpen(
            int listType,
            short slot,
            int mainExpandStage,
            int personalCargoCapacity)
        {
            if (listType == (int)InventoryListType.Main)
            {
                if (slot >= A21InventorySlotPolicy.MainQuickSlotStart
                    && slot <= A21InventorySlotPolicy.MainQuickSlotEnd)
                    return true;
                if (A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindEquipment, mainExpandStage, out var equipmentStart, out var equipmentEnd)
                    && slot >= equipmentStart && slot <= equipmentEnd)
                    return true;
                if (A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindConsumable, mainExpandStage, out var consumableStart, out var consumableEnd)
                    && slot >= consumableStart && slot <= consumableEnd)
                    return true;
                if (A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindMaterial, mainExpandStage, out var materialStart, out var materialEnd)
                    && slot >= materialStart && slot <= materialEnd)
                    return true;
                if (A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindQuest, mainExpandStage, out var questStart, out var questEnd)
                    && slot >= questStart && slot <= questEnd)
                    return true;
                if (A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindExpertJobMaterial, mainExpandStage, out var expertStart, out var expertEnd)
                    && slot >= expertStart && slot <= expertEnd)
                    return true;
                return slot >= A21InventorySlotPolicy.MainAvatarEmblemSlotStart
                    && slot <= A21InventorySlotPolicy.MainAvatarEmblemSlotEnd;
            }

            if (listType == (int)InventoryListType.Avatar)
                return slot >= A21InventorySlotPolicy.AvatarSlotStart
                    && slot <= A21InventorySlotPolicy.AvatarSlotEnd;
            if (listType == (int)InventoryListType.PersonalCargo)
                return slot >= A21InventorySlotPolicy.PersonalCargoSlotStart
                    && slot < A21InventorySlotPolicy.PersonalCargoSlotStart + personalCargoCapacity;
            if (listType == (int)InventoryListType.Pet)
                return slot >= A21InventorySlotPolicy.PetCreatureSlotStart
                    && slot <= A21InventorySlotPolicy.PetConsumableSlotEnd;
            if (listType == (int)InventoryListType.GuildMedal)
                return slot >= A21InventorySlotPolicy.GuildMedalSlotStart
                    && slot <= A21InventorySlotPolicy.GuardianGemSlotEnd;
            if (listType != (int)InventoryListType.Equipment)
                return false;

            return A21InventorySlotPolicy.TryGetEquipmentBodyKind(slot, out _);
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
