using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    // 与服务端 InventoryStackRuleService 保持同一语义：非装备 ItemCore 才可堆叠，
    // 堆叠上限由 PVF ItemMetadata.StackLimit 决定，未配置上限视为无限。
    internal static class InventoryStackRuleService
    {
        internal static bool IsStackable(ItemCore item)
        {
            return item != null
                && !item.IsEmpty
                && !item.IsEquipmentItem();
        }

        internal static bool TryGetStackLimit(ItemCore item, out int stackLimit)
        {
            stackLimit = 1;
            if (item == null || item.IsEmpty || item.ItemId <= 0)
                return false;

            if (!IsStackable(item))
                return true;

            ItemMetadata metadata;
            try
            {
                metadata = ItemMetadataResolver.Resolve(item.ItemId);
            }
            catch
            {
                return false;
            }

            if (metadata == null || !metadata.IsStackable)
                return false;

            stackLimit = metadata.StackLimit > 0 ? metadata.StackLimit : int.MaxValue;
            return true;
        }
    }
}
