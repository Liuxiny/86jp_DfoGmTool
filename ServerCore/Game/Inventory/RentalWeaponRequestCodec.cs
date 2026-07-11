using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    /// CMD 0x0372 租赁武器请求。
    /// 布局：u32 商店条目 ID + 9 字节占位 + u32 背包模板 ID + u8 天数 + u8 星价/2 + u8 价格档位。
    public static class RentalWeaponRequestCodec
    {
        public const ushort RentalWeaponDurability = 100;
        public const int RentalDurationSeconds = 86400;
        /// 租赁武器固定品级种子（协议偏移 6，与任务/初始装备一致，客户端显示 90%% 以上）。
        public const int RentalWeaponQualitySeed = (int)ItemQuality.TopQualitySeed;

        private const int ShopOffset = 0;
        private const int InventoryTemplateOffset = 13;
        private const int TailDaysOffset = 4;
        private const int TailStarCostHalfOffset = 5;
        private const int TailPriceTierOffset = 6;
        private const int MinBodyLength = 21;
        private const int MaxCostHalf = 50;
        private const int MaxDurationDays = 90;
        private const int MaxPriceTier = 30;

        public static bool TryParse(
            byte[] body,
            out uint shopWeaponId,
            out uint inventoryTemplateId,
            out int starCost,
            out byte priceTier)
        {
            shopWeaponId = 0;
            inventoryTemplateId = 0;
            starCost = 0;
            priceTier = 0;
            if (body == null || body.Length < MinBodyLength)
                return false;

            if (!TryReadUInt32(body, ShopOffset, out shopWeaponId)
                || !TryReadUInt32(body, InventoryTemplateOffset, out inventoryTemplateId)
                || InventoryTemplateOffset + TailPriceTierOffset >= body.Length)
                return false;

            var days = body[InventoryTemplateOffset + TailDaysOffset];
            if (days > MaxDurationDays)
                return false;

            var starCostHalf = body[InventoryTemplateOffset + TailStarCostHalfOffset];
            priceTier = body[InventoryTemplateOffset + TailPriceTierOffset];
            if (starCostHalf > MaxCostHalf || priceTier > MaxPriceTier)
                return false;

            if (!RentalWeaponInventoryMapper.IsValidInventoryTemplate((int)inventoryTemplateId))
                return false;

            starCost = ResolveStarCost(inventoryTemplateId, starCostHalf, priceTier);
            return starCost > 0;
        }

        public static string DescribeParseFailure(byte[] body)
        {
            if (body == null)
                return "body=null";

            if (body.Length < MinBodyLength)
                return $"bodyLen={body.Length}<min={MinBodyLength}";

            var shop = TryReadUInt32(body, ShopOffset, out var shopId) ? $"0x{shopId:X8}" : "n/a";
            var inventory = TryReadUInt32(body, InventoryTemplateOffset, out var inventoryTemplateId) ? $"0x{inventoryTemplateId:X8}" : "n/a";
            var inventoryValid = TryReadUInt32(body, InventoryTemplateOffset, out inventoryTemplateId)
                && RentalWeaponInventoryMapper.IsValidInventoryTemplate((int)inventoryTemplateId);
            var days = body[InventoryTemplateOffset + TailDaysOffset];
            var starCostHalf = body[InventoryTemplateOffset + TailStarCostHalfOffset];
            var priceTier = body[InventoryTemplateOffset + TailPriceTierOffset];

            return $"shop={shop} inv={inventory} invValid={inventoryValid} days={days} starCostHalf={starCostHalf} priceTier={priceTier}";
        }

        private static bool TryReadUInt32(byte[] body, int offset, out uint value)
        {
            value = 0;
            if (body == null || offset < 0 || offset + 4 > body.Length)
                return false;

            value = BitConverter.ToUInt32(body, offset);
            return true;
        }

        private static int ResolveStarCost(uint inventoryTemplateId, byte starCostHalf, byte priceTier)
        {
            // 真实请求通常带半价字段；缺失时按 PVF 星价和旧价格档位兜底。
            if (starCostHalf > 0)
                return starCostHalf * 2;

            var pvfPrice = RentalWeaponInventoryMapper.GetStarPrice((int)inventoryTemplateId);
            if (pvfPrice > 0)
                return pvfPrice;

            if (priceTier > 0)
                return priceTier * 8 + 2;

            var fromHighByte = (int)((inventoryTemplateId >> 24) & 0xFF) * 2;
            if (fromHighByte > 0)
                return fromHighByte;
            return (int)((inventoryTemplateId >> 16) & 0xFF) * 2;
        }
    }
}
