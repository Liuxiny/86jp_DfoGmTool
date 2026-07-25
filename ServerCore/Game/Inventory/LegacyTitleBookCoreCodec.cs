using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class LegacyTitleBookCoreCodec
    {
        internal const int RecordSize = 85;
        internal const int ListEntrySize = 22;

        internal static ItemCore DecodeRecord(byte[] source, int offset)
        {
            if (source == null || offset < 0 || offset + RecordSize > source.Length)
                return new ItemCore();
            var itemId = BitConverter.ToInt32(source, offset + 2);
            if (itemId <= 0)
                return new ItemCore();
            var core = ItemCore.Create(ItemCore.KindEquipment, itemId);
            core.Value = BitConverter.ToInt32(source, offset + 6);
            core.Attr = source[offset + 10];
            core.Durability = BitConverter.ToUInt16(source, offset + 11);
            core.SealFlag = source[offset + 13];
            core.EnchantCardId = BitConverter.ToInt32(source, offset + 14);
            core.EnchantUpgradeCount = source[offset + 18];
            core.AmplifyType = source[offset + 19];
            core.AmplifyValue = BitConverter.ToUInt16(source, offset + 20);
            core.Marker16 = BitConverter.ToInt32(source, offset + 22);
            core.SetChronicleOptions(DecodeChronicle(source, offset + 26));
            core.ExpireTime = BitConverter.ToInt32(source, offset + 43);
            DecodeTail(core, source, offset + 47);
            core.EquipmentLockId = source[offset + 84];
            return core;
        }

        internal static bool TryDecodeListEntry(byte[] source, int offset, out short slot, out ItemCore core)
        {
            slot = 0;
            core = new ItemCore();
            if (source == null || offset < 0 || offset + ListEntrySize > source.Length)
                return false;
            slot = checked((short)BitConverter.ToUInt16(source, offset));
            var itemId = BitConverter.ToInt32(source, offset + 2);
            if (itemId <= 0)
                return true;
            core = ItemCore.Create(ItemCore.KindEquipment, itemId);
            core.Value = BitConverter.ToInt32(source, offset + 6);
            core.Attr = source[offset + 10];
            core.Durability = BitConverter.ToUInt16(source, offset + 11);
            core.SealFlag = source[offset + 13];
            core.EnchantCardId = BitConverter.ToInt32(source, offset + 14);
            core.EnchantUpgradeCount = source[offset + 18];
            core.AmplifyType = source[offset + 19];
            core.AmplifyValue = BitConverter.ToUInt16(source, offset + 20);
            return true;
        }

        internal static byte[] EncodeRecord(short slot, ItemCore core)
        {
            var result = new byte[RecordSize];
            if (core == null || core.IsEmpty)
                return result;
            BitConverter.GetBytes(slot).CopyTo(result, 0);
            BitConverter.GetBytes(core.ItemId).CopyTo(result, 2);
            BitConverter.GetBytes(core.Value).CopyTo(result, 6);
            result[10] = core.Attr;
            BitConverter.GetBytes(core.Durability).CopyTo(result, 11);
            result[13] = core.SealFlag;
            BitConverter.GetBytes(core.EnchantCardId).CopyTo(result, 14);
            result[18] = core.EnchantUpgradeCount;
            result[19] = core.AmplifyType;
            BitConverter.GetBytes(core.AmplifyValue).CopyTo(result, 20);
            BitConverter.GetBytes(core.Marker16).CopyTo(result, 22);
            EncodeChronicle(core, result, 26);
            BitConverter.GetBytes(core.ExpireTime).CopyTo(result, 43);
            EncodeTail(core, result, 47);
            result[84] = core.EquipmentLockId;
            return result;
        }

        private static ChronicleOption[] DecodeChronicle(byte[] source, int offset)
        {
            var count = Math.Min(source[offset], (byte)2);
            var result = new ChronicleOption[count];
            for (var i = 0; i < count; i++)
            {
                var current = offset + 1 + i * 8;
                result[i] = new ChronicleOption
                {
                    OptionId = BitConverter.ToInt32(source, current),
                    CharacJob = source[current + 4], FirstGrowType = source[current + 5],
                    EquipmentType = source[current + 6], OptionNo = source[current + 7],
                };
            }
            return result;
        }

        private static void EncodeChronicle(ItemCore core, byte[] target, int offset)
        {
            var options = core.ChronicleOptions;
            var count = Math.Min(options.Count, 2);
            target[offset] = (byte)count;
            for (var i = 0; i < count; i++)
            {
                var current = offset + 1 + i * 8;
                var option = options[i];
                BitConverter.GetBytes(option.OptionId).CopyTo(target, current);
                target[current + 4] = option.CharacJob; target[current + 5] = option.FirstGrowType;
                target[current + 6] = option.EquipmentType; target[current + 7] = option.OptionNo;
            }
        }

        private static void DecodeTail(ItemCore core, byte[] source, int offset)
        {
            core.EmblemSocketCount = source[offset];
            core.EmblemId1 = BitConverter.ToInt32(source, offset + 1);
            core.EmblemId2 = BitConverter.ToInt32(source, offset + 5);
            core.Rune = BitConverter.ToUInt16(source, offset + 9);
            core.RandomOption0.Type = source[offset + 12]; core.RandomOption1.Type = source[offset + 13]; core.RandomOption2.Type = source[offset + 14];
            core.RandomOption0.Value1 = source[offset + 15]; core.RandomOption1.Value1 = source[offset + 16]; core.RandomOption2.Value1 = source[offset + 17];
            core.RandomOption0.Value2 = source[offset + 18]; core.RandomOption1.Value2 = source[offset + 19]; core.RandomOption2.Value2 = source[offset + 20];
            core.RandomOptionState = source[offset + 21]; core.RandomOptionChangedIndex = source[offset + 22]; core.RandomOptionChangeState = source[offset + 23];
            core.RandomOptionChange.Type = source[offset + 24]; core.RandomOptionChange.Value1 = source[offset + 25]; core.RandomOptionChange.Value2 = source[offset + 26];
            core.GenuineUpgrade = source[offset + 27]; core.EmancipateEquipmentLevel = source[offset + 28]; core.TradeRestriction = source[offset + 29];
            core.TailUnknown0 = BitConverter.ToUInt16(source, offset + 30); core.TailUnknown1 = source[offset + 32]; core.TailUnknown2 = source[offset + 33];
            core.TailUnknown3 = source[offset + 34]; core.RemainUseCount = source[offset + 35]; core.SortLockFlag = source[offset + 36];
        }

        private static void EncodeTail(ItemCore core, byte[] target, int offset)
        {
            target[offset] = core.EmblemSocketCount;
            BitConverter.GetBytes(core.EmblemId1).CopyTo(target, offset + 1); BitConverter.GetBytes(core.EmblemId2).CopyTo(target, offset + 5);
            BitConverter.GetBytes(core.Rune).CopyTo(target, offset + 9);
            target[offset + 12] = core.RandomOption0.Type; target[offset + 13] = core.RandomOption1.Type; target[offset + 14] = core.RandomOption2.Type;
            target[offset + 15] = core.RandomOption0.Value1; target[offset + 16] = core.RandomOption1.Value1; target[offset + 17] = core.RandomOption2.Value1;
            target[offset + 18] = core.RandomOption0.Value2; target[offset + 19] = core.RandomOption1.Value2; target[offset + 20] = core.RandomOption2.Value2;
            target[offset + 21] = core.RandomOptionState; target[offset + 22] = core.RandomOptionChangedIndex; target[offset + 23] = core.RandomOptionChangeState;
            target[offset + 24] = core.RandomOptionChange.Type; target[offset + 25] = core.RandomOptionChange.Value1; target[offset + 26] = core.RandomOptionChange.Value2;
            target[offset + 27] = core.GenuineUpgrade; target[offset + 28] = core.EmancipateEquipmentLevel; target[offset + 29] = core.TradeRestriction;
            BitConverter.GetBytes(core.TailUnknown0).CopyTo(target, offset + 30); target[offset + 32] = core.TailUnknown1; target[offset + 33] = core.TailUnknown2;
            target[offset + 34] = core.TailUnknown3; target[offset + 35] = core.RemainUseCount; target[offset + 36] = core.SortLockFlag;
        }
    }
}
