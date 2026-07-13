using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.GameWorld;
using GmPvfLib;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    /// 校验 chn_rental 背包模板，并读取租赁星价。
    public static class RentalWeaponInventoryMapper
    {
        private sealed class RentalWeaponIdentity
        {
            public int StarPrice { get; set; }
        }

        private static readonly object CacheLock = new object();
        private static Lazy<Dictionary<int, RentalWeaponIdentity>> IdentityById = CreateIdentityIndex();

        internal static void ResetForPvfChange()
        {
            lock (CacheLock)
                IdentityById = CreateIdentityIndex();
        }

        private static Lazy<Dictionary<int, RentalWeaponIdentity>> CreateIdentityIndex()
        {
            return new Lazy<Dictionary<int, RentalWeaponIdentity>>(BuildIdentityIndex);
        }

        public static bool IsValidInventoryTemplate(int itemTemplateId)
        {
            if (itemTemplateId <= 0)
                return false;

            return IdentityById.Value.ContainsKey(itemTemplateId);
        }

        public static int GetStarPrice(int inventoryTemplateId)
        {
            if (IdentityById.Value.TryGetValue(inventoryTemplateId, out var identity) && identity.StarPrice > 0)
                return identity.StarPrice;

            var buyGold = ItemMetadataResolver.Resolve(inventoryTemplateId).BuyGold;
            return buyGold > 0 ? buyGold : 0;
        }

        private static Dictionary<int, RentalWeaponIdentity> BuildIdentityIndex()
        {
            var byId = new Dictionary<int, RentalWeaponIdentity>();
            var lst = LstFile.Parse(PvfArchiveAccessor.ReadText("equipment/equipment.lst"));
            foreach (var entry in lst.Entries)
            {
                if (entry.FilePath.IndexOf("chn_rental_", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var equipment = EquipmentFile.Parse(
                    PvfArchiveAccessor.ReadText(System.IO.Path.Combine("equipment", entry.FilePath)));

                byId[entry.Id] = new RentalWeaponIdentity
                {
                    StarPrice = equipment.Price,
                };
            }

            return byId;
        }
    }
}
