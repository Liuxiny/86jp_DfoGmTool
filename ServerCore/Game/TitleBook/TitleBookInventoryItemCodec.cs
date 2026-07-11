using DfoGmTool.ServerCore.Game.Inventory;
using System;

namespace DfoGmTool.ServerCore.Game.TitleBook
{
    public static class TitleBookInventoryItemCodec
    {
        public const int CommonNetworkSize = 84;
        public const int PersistedRecordSize = CommonNetworkSize + 1;
        public const int TitleBookListEntrySize = 22;

        internal static TitleBookInventoryItem FromItemRecord(int category, ushort bookIndex, SqliteInventoryStore.ItemRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var extra = ItemExtraView.Parse(record.ExtraJson);

            return new TitleBookInventoryItem
            {
                Category = category,
                BookIndex = bookIndex,
                Slot = unchecked((ushort)record.SlotIndex),
                ItemId = record.ItemTemplateId,
                Value = record.StackCount,
                Attr = extra.Equipment.ExtData0,
                Durability = record.Durability,
                SealFlag = record.SealFlag,
                EnchantIndex = extra.Equipment.EnchantCardId,
                EnchantUpgradeCount = extra.Equipment.EnchantUpgradeCount,
                AmplifyType = extra.Equipment.AmplifyType,
                AmplifyValue = extra.Equipment.AmplifyValue,
                Marker16 = record.Marker16,
                Chronicle = DecodeChronicle(extra.Raw84.MiddleData1A),
                ExpireTime = record.ExpireTime,
                TailData = Normalize(extra.Raw84.TailData2F, 37),
                EquipmentLockId = record.EquipmentLockId,
            };
        }

        internal static string ToExtraJson(TitleBookInventoryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var builder = new ItemExtraViewBuilder();
            builder.Equipment.ExtData0 = item.Attr;
            builder.Equipment.EnchantCardId = item.EnchantIndex;
            builder.Equipment.EnchantUpgradeCount = item.EnchantUpgradeCount;
            builder.Equipment.AmplifyType = item.AmplifyType;
            builder.Equipment.AmplifyValue = item.AmplifyValue;
            builder.Equipment.MiddleData1A = EncodeChronicle(item.Chronicle);
            builder.Equipment.TailData2F = Normalize(item.TailData, 37);
            builder.Equipment.JewelSocket = new byte[30];
            return builder.Build().Serialize();
        }

        internal static string InferItemKind(TitleBookInventoryItem item)
        {
            if (item == null || item.ItemId <= 0)
                return "special";

            if (item.ExpireTime != 0)
                return "special";

            return item.Marker16 == 0 ? "stackable" : "equipment";
        }

        public static TitleBookListEntrySnapshot ToListEntry(TitleBookInventoryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            return new TitleBookListEntrySnapshot
            {
                SlotIndex = item.Slot,
                ItemId = item.ItemId,
                Value = item.Value,
                Attr = item.Attr,
                Durability = item.Durability,
                SealFlag = item.SealFlag,
                EnchantIndex = item.EnchantIndex,
                EnchantUpgradeCount = item.EnchantUpgradeCount,
                AmplifyType = item.AmplifyType,
                AmplifyValue = item.AmplifyValue,
            };
        }

        public static byte[] Serialize(TitleBookInventoryItem item)
        {
            if (item == null || item.IsEmpty)
                return new byte[PersistedRecordSize];

            var buf = new byte[PersistedRecordSize];
            WriteInt16(buf, 0, unchecked((short)item.Slot));
            WriteInt32(buf, 2, item.ItemId);
            WriteInt32(buf, 6, item.Value);
            buf[10] = item.Attr;
            WriteUInt16(buf, 11, item.Durability);
            buf[13] = item.SealFlag;
            WriteInt32(buf, 14, item.EnchantIndex);
            buf[18] = item.EnchantUpgradeCount;
            buf[19] = item.AmplifyType;
            WriteUInt16(buf, 20, item.AmplifyValue);
            WriteInt32(buf, 22, item.Marker16);
            Buffer.BlockCopy(EncodeChronicle(item.Chronicle), 0, buf, 26, 17);
            WriteInt32(buf, 43, item.ExpireTime);
            Buffer.BlockCopy(Normalize(item.TailData, 37), 0, buf, 47, 37);
            buf[CommonNetworkSize] = item.EquipmentLockId;
            return buf;
        }

        public static TitleBookInventoryItem Deserialize(int category, ushort bookIndex, byte[] record)
        {
            var data = Normalize(record, PersistedRecordSize);
            var itemId = BitConverter.ToInt32(data, 2);
            if (itemId <= 0)
                return CreateEmpty(category, bookIndex);

            return new TitleBookInventoryItem
            {
                Category = category,
                BookIndex = bookIndex,
                Slot = unchecked((ushort)BitConverter.ToInt16(data, 0)),
                ItemId = itemId,
                Value = BitConverter.ToInt32(data, 6),
                Attr = data[10],
                Durability = BitConverter.ToUInt16(data, 11),
                SealFlag = data[13],
                EnchantIndex = BitConverter.ToInt32(data, 14),
                EnchantUpgradeCount = data[18],
                AmplifyType = data[19],
                AmplifyValue = BitConverter.ToUInt16(data, 20),
                Marker16 = BitConverter.ToInt32(data, 22),
                Chronicle = DecodeChronicle(Slice(data, 26, 17)),
                ExpireTime = BitConverter.ToInt32(data, 43),
                TailData = Slice(data, 47, 37),
                EquipmentLockId = data[CommonNetworkSize],
            };
        }

        public static TitleBookInventoryItem CreateEmpty(int category, ushort bookIndex)
        {
            return new TitleBookInventoryItem
            {
                Category = category,
                BookIndex = bookIndex,
                Slot = bookIndex,
                ItemId = -1,
            };
        }

        public static TitleBookChronicleData DecodeChronicle(byte[] raw)
        {
            var data = Normalize(raw, 17);
            var chronicle = new TitleBookChronicleData { Count = data[0] };
            var count = Math.Min(chronicle.Count, (byte)2);
            var off = 1;
            for (var i = 0; i < count; i++)
            {
                chronicle.Options.Add(new TitleBookChronicleOption
                {
                    OptionId = BitConverter.ToInt32(data, off),
                    CharacJob = data[off + 4],
                    FirstGrowType = data[off + 5],
                    EquipmentType = data[off + 6],
                    OptionNo = data[off + 7],
                });
                off += 8;
            }
            return chronicle;
        }

        public static byte[] EncodeChronicle(TitleBookChronicleData chronicle)
        {
            var data = new byte[17];
            if (chronicle == null)
                return data;

            var count = Math.Min(chronicle.Options.Count, 2);
            data[0] = (byte)Math.Min(chronicle.Count > 0 ? chronicle.Count : count, 2);
            var off = 1;
            for (var i = 0; i < count; i++)
            {
                var option = chronicle.Options[i];
                BitConverter.GetBytes(option.OptionId).CopyTo(data, off);
                data[off + 4] = option.CharacJob;
                data[off + 5] = option.FirstGrowType;
                data[off + 6] = option.EquipmentType;
                data[off + 7] = option.OptionNo;
                off += 8;
            }
            return data;
        }

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            var result = new byte[length];
            if (data == null || offset >= data.Length)
                return result;

            Buffer.BlockCopy(data, offset, result, 0, Math.Min(length, data.Length - offset));
            return result;
        }

        private static byte[] Normalize(byte[] data, int length)
        {
            var result = new byte[length];
            if (data == null)
                return result;

            Buffer.BlockCopy(data, 0, result, 0, Math.Min(length, data.Length));
            return result;
        }

        private static void WriteInt16(byte[] buf, int offset, short value)
        {
            BitConverter.GetBytes(value).CopyTo(buf, offset);
        }

        private static void WriteInt32(byte[] buf, int offset, int value)
        {
            BitConverter.GetBytes(value).CopyTo(buf, offset);
        }

        private static void WriteUInt16(byte[] buf, int offset, ushort value)
        {
            BitConverter.GetBytes(value).CopyTo(buf, offset);
        }
    }
}
