using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class InventoryService
    {
        internal const short MainVirtualCurrencySlotStart = 0;
        internal const short MainVirtualCurrencySlotEnd = 2;
        internal const short MainVirtualCubeSlotStart = 354;
        internal const short MainVirtualCubeSlotEnd = 359;

        internal static bool IsReservedMainSlot(short slotIndex)
            => slotIndex == 352 || slotIndex == 353;
    }

    internal static class ItemSlotBoundService
    {
        internal static bool TryResolveItemKindForMigration(InventoryListType listType, short slot, int itemId, out byte kind)
        {
            if (listType == InventoryListType.Equipment)
            {
                if (slot <= 10) { kind = ItemCore.KindAvatar; return true; }
                if (slot <= 23 || slot == 29) { kind = ItemCore.KindEquipment; return true; }
                if (slot == 24) { kind = ItemCore.KindCreature; return true; }
                if (slot >= 25 && slot <= 27) { kind = ItemCore.KindCreatureEquipment; return true; }
            }
            if (listType == InventoryListType.Avatar) { kind = ItemCore.KindAvatar; return slot >= 0 && slot <= 209; }
            if (listType == InventoryListType.Pet)
            {
                if (slot <= 139) { kind = ItemCore.KindCreature; return true; }
                if (slot <= 188) { kind = ItemCore.KindCreatureEquipment; return true; }
                if (slot <= 239) { kind = ItemCore.KindCreatureConsumable; return true; }
            }
            if (listType == InventoryListType.Main)
            {
                if (slot <= 2) { kind = ItemCore.KindSpecialMaterial; return true; }
                if (slot >= 9 && slot <= 64) { kind = ItemCore.KindEquipment; return true; }
                if (slot >= 65 && slot <= 120) { kind = ItemCore.KindConsumable; return true; }
                if (slot >= 121 && slot <= 176) { kind = ItemCore.KindMaterial; return true; }
                if (slot >= 177 && slot <= 232) { kind = ItemCore.KindQuest; return true; }
                if (slot >= 233 && slot <= 288) { kind = ItemCore.KindExpertJobMaterial; return true; }
                if (slot >= 289 && slot <= 351) { kind = ItemCore.KindAvatarEmblem; return true; }
            }
            return TryResolveFromPvf(itemId, out kind);
        }

        private static bool TryResolveFromPvf(int itemId, out byte kind)
        {
            kind = ItemCore.KindUnknown;
            var metadata = ItemMetadataResolver.Resolve(itemId);
            if (metadata == null || metadata.ItemKind == "special") return false;
            if (metadata.ItemKind == "equipment")
            {
                if (ItemMetadataResolver.IsAvatarMetadata(metadata)) kind = ItemCore.KindAvatar;
                else if (ItemMetadataResolver.IsPetCreatureMetadata(metadata)) kind = ItemCore.KindCreature;
                else if (ItemMetadataResolver.IsPetArtifactMetadata(metadata)) kind = ItemCore.KindCreatureEquipment;
                else kind = ItemCore.KindEquipment;
                return true;
            }
            if (!metadata.IsStackable) return false;
            if (ItemMetadataResolver.IsPetConsumableItem(metadata)) kind = ItemCore.KindCreatureConsumable;
            else
            {
                kind = ItemMetadataResolver.ResolvePvfTypeTag(metadata) switch
                {
                    "material" => ItemCore.KindMaterial,
                    "quest" => ItemCore.KindQuest,
                    "material expert job" => ItemCore.KindExpertJobMaterial,
                    "avatar emblem" => ItemCore.KindAvatarEmblem,
                    _ => ItemCore.KindConsumable,
                };
            }
            return true;
        }
    }

    internal static class InventoryMainVirtualCountRepository
    {
        internal static void UpsertCurrencySlot(SqliteConnection connection, SqliteTransaction transaction, int characterId, short slot, int count)
        {
            var core = new ItemCore { ItemKind = ItemCore.KindSpecialMaterial, ItemId = slot, Count = Math.Max(0, count) };
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core)
VALUES('character',@cid,@cid,0,@slot,@core)
ON CONFLICT(owner_scope,owner_id,list_type,slot_index) DO UPDATE SET item_core=excluded.item_core,updated_at=CURRENT_TIMESTAMP;";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core.ToBytes());
            command.ExecuteNonQuery();
        }
    }

    internal static class AvatarDetailRepository
    {
        internal static long AllocateAvatarUid(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO character_avatar_uid_sequence DEFAULT VALUES; SELECT last_insert_rowid();";
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }
}
