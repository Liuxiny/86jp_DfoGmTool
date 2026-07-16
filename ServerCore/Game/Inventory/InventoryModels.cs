// GM瘦身拷贝: 相对服务端原版仅保留 ItemQuality 与 InventoryMutationResult; 删除了移动/排序/
// 穿脱(EquipOutcome)/金库升级/宠物孵化改名/修理/礼盒(Booster*)/镶嵌徽章等全部其余模型类;
// 保留成员与原版逐字一致
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    // 装备实例的品质常量。服务端生成装备统一写这个种子:
    // 999999998 = 最上级(真机实证); 0 会导致修理后装备消失, 禁用。
    public static class ItemQuality
    {
        public const uint TopQualitySeed = 999999998u;

        public static uint ResolveSeed(ItemQualityMode mode)
        {
            if (mode == ItemQualityMode.Top)
                return TopQualitySeed;

            uint seed;
            do
            {
                seed = unchecked((uint)RandomNumberGenerator.GetInt32(1, int.MaxValue));
            }
            while (seed == TopQualitySeed);

            return seed;
        }
    }

    public enum ItemQualityMode
    {
        Random = 0,
        Top = 1,
    }

    public sealed class ItemGrantResult
    {
        public bool Success { get; internal set; }

        public string Error { get; internal set; }

        public int ItemTemplateId { get; internal set; }

        public int RequestedCount { get; internal set; }

        public int GrantedCount { get; internal set; }

        public InventoryListType ListType { get; internal set; }

        public short AssignedSlot { get; internal set; } = -1;

        public int ExpireTime { get; internal set; }

        public List<short> AffectedSlots { get; } = new List<short>();
    }

    public sealed class InventoryMutationResult
    {
        public InventoryListType ListType { get; set; }

        public short SlotIndex { get; set; }

        public int ItemTemplateId { get; set; }

        public int RemainingStackCount { get; set; }

        public int InstanceValue { get; set; }

        public ushort Durability { get; set; }

        public byte ExtData0 { get; set; }

        public int UpdatedGold { get; set; }

        public int UpdatedSp { get; set; }

        public int UpdatedCoin { get; set; }

        public int UpdatedTokenCera { get; set; }

        public int UpdatedHappyTokenCera { get; set; }

        public short RequestedCount { get; set; }

        public short AppliedCount { get; set; }

        // 本次购买是否扣了金币(用于商城回包决定是否刷新主背包 slot0 金币显示)。
        public bool GoldSpent { get; set; }

        // 契约等道具购买即消耗，不入库；为 true 时跳过 ITEM_LIST 更新通知。
        public bool ConsumedOnPurchase { get; set; }

        public int CostItemTemplateId { get; set; }

        public int CostItemNewStackCount { get; set; }

        public short CostItemSlotIndex { get; set; }

        public List<InventoryMutationResult> ExtraResults { get; } = new List<InventoryMutationResult>();

        public int PetCreatureKey { get; set; }

        public int PetSatietyBefore { get; set; }

        public int PetSatietyAfter { get; set; }

        public bool PetSatietyChanged { get; set; }

        public bool NameTagEquipped { get; set; }
    }
}
