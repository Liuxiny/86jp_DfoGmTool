// GM瘦身拷贝: 相对服务端原版删除了 TryBuyItem, TryPickupRentalWeapon, TryPickupItem, TrySellItem;
// 保留槽段常量与 TryPickupItemCore, 保留成员与原版逐字一致
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed partial class SqliteInventoryStore
    {
        internal const int QuickSlotStart = 3;
        internal const int QuickSlotEnd = 8;
        internal const int RentalBagSlotStart = 9;
        internal const int RentalBagSlotEnd = 64;

        // 宠物栏(list 7)"宠物"本体分页槽段(category 5): slot 0..139 共 140 格(实测计数)。
        // 其后 宠物装备=140..188(cat6)、宠物耗品=189..237(cat7)。新购宠物从本页首格开始填。
        // Client pet inventory pages share list 7 but use separate slot ranges:
        // category 5 = pets, category 6 = pet equipment, category 7 = pet consumables.
        internal const int PetInventorySlotStart = 0;
        internal const int PetInventorySlotEnd = 139;
        internal const int PetEquipmentSlotStart = 140;
        internal const int PetEquipmentSlotEnd = 188;
        internal const int PetConsumableSlotStart = 189;
        internal const int PetConsumableSlotEnd = 237;
        internal const int AvatarEmblemSlotStart = 289;
        internal const int AvatarEmblemSlotEnd = 344;

        internal bool TryPickupItemCore(
            SqliteConnection connection, SqliteTransaction transaction,
            int characterId, int accountId,
            int itemTemplateId, int stackCount, out short assignedSlot)
        {
            assignedSlot = -1;

            // 晶块走账号级存储, 不进 character_items
            if (CurrencyService.IsCubeFragment(itemTemplateId))
            {
                CurrencyService.AddCubeFragment(connection, transaction, accountId, itemTemplateId, stackCount);
                assignedSlot = (short)CurrencyService.GetCubeFragmentSlot(itemTemplateId);
                return true;
            }

            // 复活币固定 slot1; 行被扣光删除后重建仍回 slot1(必须在 metadata Resolve 之前, 证据见 ReviveCoinService)
            if (itemTemplateId == Game.ReviveCoin.ReviveCoinService.ItemId)
            {
                var existingCoin = _db.FindItemByTemplateIdInRange(
                    connection, transaction, characterId, InventoryListType.Main,
                    Game.ReviveCoin.ReviveCoinService.ItemId,
                    Game.ReviveCoin.ReviveCoinService.WalletSlot, Game.ReviveCoin.ReviveCoinService.WalletSlot);
                if (existingCoin != null)
                {
                    _db.UpdateStackCount(connection, transaction, existingCoin.ItemUid, existingCoin.StackCount + stackCount);
                }
                else
                {
                    _db.InsertCharacterItem(
                        connection, transaction, characterId, InventoryListType.Main, Game.ReviveCoin.ReviveCoinService.WalletSlot,
                        Game.ReviveCoin.ReviveCoinService.ItemId, "stackable", stackCount, stackCount, 0, 0, 0, 0, 0, 0, "{}");
                }
                assignedSlot = Game.ReviveCoin.ReviveCoinService.WalletSlot;
                return true;
            }

            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            if (metadata.ItemKind == "special")
                return false;

            if ((CharmInventoryPolicy.IsCharmItem(itemTemplateId) && Math.Max(1, stackCount) > 1)
                || !CharmInventoryPolicy.CanEnterMain(connection, transaction, characterId, itemTemplateId))
                return false;

            bool isConsumable = metadata.IsStackable
                && metadata.StackableType != null
                && metadata.StackableType.IndexOf("[waste]", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (metadata.IsStackable)
            {
                if (isConsumable)
                {
                    var existingQuick = _db.FindItemByTemplateIdInRange(connection, transaction, characterId, InventoryListType.Main, itemTemplateId, QuickSlotStart, QuickSlotEnd);
                    if (existingQuick != null && (metadata.StackLimit <= 0 || existingQuick.StackCount + stackCount <= metadata.StackLimit))
                    {
                        _db.UpdateStackCount(connection, transaction, existingQuick.ItemUid, existingQuick.StackCount + stackCount);
                        assignedSlot = existingQuick.SlotIndex;
                        return true;
                    }
                }

                var existing = _db.FindItemByTemplateId(connection, transaction, characterId, InventoryListType.Main, itemTemplateId);
                if (existing != null && (metadata.StackLimit <= 0 || existing.StackCount + stackCount <= metadata.StackLimit))
                {
                    _db.UpdateStackCount(connection, transaction, existing.ItemUid, existing.StackCount + stackCount);
                    assignedSlot = existing.SlotIndex;
                    return true;
                }
            }

            int slotStart, slotEnd;
            metadata.GetSlotRange(out slotStart, out slotEnd);

            if (isConsumable)
            {
                var quickSlot = _db.FindEmptySlot(connection, transaction, characterId, InventoryListType.Main, QuickSlotStart, QuickSlotEnd);
                if (quickSlot >= 0)
                {
                    _db.InsertCharacterItem(
                        connection, transaction, characterId, InventoryListType.Main, (short)quickSlot,
                        itemTemplateId, metadata.ItemKind, stackCount, stackCount,
                        metadata.Durability, 0, 0, 0, 0, 0, "{}");
                    assignedSlot = (short)quickSlot;
                    return true;
                }
            }

            var targetSlot = _db.FindEmptySlot(connection, transaction, characterId, InventoryListType.Main, slotStart, slotEnd);
            if (targetSlot < 0)
                return false;

            var qualitySeed = InventoryDbPrimitives.GenerateInstanceValue(itemTemplateId, targetSlot);
            var dbStackCount = metadata.IsStackable ? stackCount : qualitySeed;
            var dbInstanceValue = metadata.IsStackable ? stackCount : qualitySeed;
            var sealFlag = metadata.IsSealed ? (byte)1 : (byte)0;
            _db.InsertCharacterItem(
                connection, transaction, characterId, InventoryListType.Main, (short)targetSlot,
                itemTemplateId, metadata.ItemKind, dbStackCount, dbInstanceValue,
                metadata.Durability, sealFlag, 0, 0, metadata.IsStackable ? 0 : -1, 0, "{}");
            assignedSlot = (short)targetSlot;
            return true;
        }
    }
}
