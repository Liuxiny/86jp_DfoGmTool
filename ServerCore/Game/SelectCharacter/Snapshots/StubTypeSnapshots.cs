using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.SelectCharacter
{
    public sealed class SkillPointSlotEntrySnapshot
    {
        public byte SkillType { get; set; }
        public ushort Points { get; set; }
    }

    public sealed class RentalItemSnapshot
    {
        // ItemId 是租赁商店条目ID，仅供服务端恢复映射；协议包使用背包模板ID。
        public uint ItemId { get; set; }
        public uint InventoryTemplateId { get; set; }
        public uint ExpireTime { get; set; }
    }

    public sealed class RentalInfoSnapshot
    {
        public const uint DefaultRentalId = 891;

        public uint RentalId { get; set; } = DefaultRentalId;
        public List<RentalItemSnapshot> Items { get; } = new List<RentalItemSnapshot>();

        public int RemoveExpired(uint nowUnixSeconds)
        {
            var removed = 0;
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].ExpireTime == 0 || Items[i].ExpireTime <= nowUnixSeconds)
                {
                    Items.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        public void ReplaceItems(IEnumerable<RentalItemSnapshot> items)
        {
            Items.Clear();
            if (items == null)
                return;

            foreach (var item in items)
            {
                if (item == null || item.ItemId == 0 || item.ExpireTime == 0)
                    continue;

                UpsertItem(item.ItemId, item.InventoryTemplateId, item.ExpireTime);
            }
        }

        public void UpsertItem(uint itemId, uint inventoryTemplateId, uint expireTime, params uint[] legacyItemIds)
        {
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                var currentId = Items[i].ItemId;
                var currentInventoryId = Items[i].InventoryTemplateId;
                // 去重以背包模板为准；旧数据可能只有商店条目ID，需要兼容迁移。
                if ((inventoryTemplateId != 0 && currentInventoryId == inventoryTemplateId)
                    || (inventoryTemplateId != 0 && currentId == inventoryTemplateId)
                    || (inventoryTemplateId != 0 && currentInventoryId == 0 && currentId == itemId)
                    || (inventoryTemplateId == 0 && currentId == itemId)
                    || Contains(legacyItemIds, currentId))
                    Items.RemoveAt(i);
            }

            Items.Add(new RentalItemSnapshot { ItemId = itemId, InventoryTemplateId = inventoryTemplateId, ExpireTime = expireTime });
        }

        private static bool Contains(uint[] values, uint value)
        {
            if (values == null)
                return false;

            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                    return true;
            }

            return false;
        }

        // 旧 character_init_bodies(0x0357) 存储编码。生产读写已迁到 character_rental_items 表;
        // 保留解析仅供迁移 22 与自测使用。
        public static void ParseStorageBody(byte[] body, RentalInfoSnapshot rental)
        {
            if (rental == null)
                return;

            rental.Items.Clear();
            if (body == null || body.Length < 8)
                return;

            uint count;
            int off;
            if (body.Length >= 12 && BitConverter.ToUInt32(body, 4) == DefaultRentalId)
            {
                rental.RentalId = DefaultRentalId;
                count = BitConverter.ToUInt32(body, 8);
                off = 12;
            }
            else
            {
                rental.RentalId = BitConverter.ToUInt32(body, 0);
                count = BitConverter.ToUInt32(body, 4);
                off = 8;
            }

            // 内部存储新格式：租赁ID + 数量 + (商店条目ID + 背包模板ID + 到期秒)*。
            // 旧格式只有商店条目ID + 到期秒，解析时保留兼容。
            var hasInventoryTemplateId = body.Length >= off + count * 12;
            for (uint i = 0; i < count && off + 8 <= body.Length; i++)
            {
                var inventoryTemplateId = hasInventoryTemplateId
                    ? BitConverter.ToUInt32(body, off + 4)
                    : 0;
                var expireOffset = hasInventoryTemplateId ? off + 8 : off + 4;
                rental.Items.Add(new RentalItemSnapshot
                {
                    ItemId = BitConverter.ToUInt32(body, off),
                    InventoryTemplateId = inventoryTemplateId,
                    ExpireTime = BitConverter.ToUInt32(body, expireOffset),
                });
                off += hasInventoryTemplateId ? 12 : 8;
            }
        }

        public static byte[] BuildStorageBody(RentalInfoSnapshot rental)
        {
            // 只写服务端内部存储；0x0357 协议包由 RentalInfoBodyBuilder 单独构建。
            var info = rental ?? new RentalInfoSnapshot();
            var itemCount = info.Items.Count;
            var storage = new byte[8 + itemCount * 12];
            Buffer.BlockCopy(BitConverter.GetBytes(info.RentalId), 0, storage, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)itemCount), 0, storage, 4, 4);
            for (var i = 0; i < itemCount; i++)
            {
                var off = 8 + i * 12;
                Buffer.BlockCopy(BitConverter.GetBytes(info.Items[i].ItemId), 0, storage, off, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(info.Items[i].InventoryTemplateId), 0, storage, off + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(info.Items[i].ExpireTime), 0, storage, off + 8, 4);
            }

            return storage;
        }
    }
}
