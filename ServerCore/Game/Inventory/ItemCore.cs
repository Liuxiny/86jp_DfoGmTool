using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class ItemCore
    {
        public const int Size = 82;
        public const int Marker16Default = -1;
        public const byte RandomOptionChangedIndexDefault = 0xFF;

        public const byte KindUnknown = 0;
        public const byte KindEquipment = 1;
        public const byte KindConsumable = 2;
        public const byte KindMaterial = 3;
        public const byte KindQuest = 4;
        public const byte KindCreature = 5;
        public const byte KindCreatureEquipment = 6;
        public const byte KindCreatureConsumable = 7;
        public const byte KindAvatar = 8;
        public const byte KindAvatarEmblem = 9;
        public const byte KindExpertJobMaterial = 10;
        public const byte KindSpecialMaterial = 11;

        public const int ItemKindOffset = 0;
        public const int ItemIdOffset = 1;
        public const int ValueOffset = 5;
        public const int AttrOffset = 9;
        public const int DurabilityOffset = 10;
        public const int SealFlagOffset = 12;
        public const int EnchantCardIdOffset = 13;
        public const int EnchantUpgradeCountOffset = 17;
        public const int AmplifyTypeOffset = 18;
        public const int AmplifyValueOffset = 19;
        public const int Marker16Offset = 21;
        public const int ChronicleOption0Offset = 25;
        public const int ChronicleOption1Offset = 33;
        public const int ExpireTimeOffset = 41;
        public const int EmblemSocketCountOffset = 45;
        public const int EmblemId1Offset = 46;
        public const int EmblemId2Offset = 50;
        public const int RuneOffset = 54;
        public const int RandomOption0Offset = 56;
        public const int RandomOption1Offset = 59;
        public const int RandomOption2Offset = 62;
        public const int RandomOptionStateOffset = 65;
        public const int RandomOptionChangedIndexOffset = 66;
        public const int RandomOptionChangeStateOffset = 67;
        public const int RandomOptionChangeTypeOffset = 68;
        public const int RandomOptionChangeValue1Offset = 69;
        public const int RandomOptionChangeValue2Offset = 70;
        public const int GenuineUpgradeOffset = 71;
        public const int EmancipateEquipmentLevelOffset = 72;
        public const int TradeRestrictionOffset = 73;
        public const int TailUnknown0Offset = 74;
        public const int TailUnknown1Offset = 76;
        public const int TailUnknown2Offset = 77;
        public const int TailUnknown3Offset = 78;
        public const int RemainUseCountOffset = 79;
        public const int SortLockFlagOffset = 80;
        public const int EquipmentLockIdOffset = 81;

        public ItemCore()
        {
            ChronicleOption0 = new ChronicleOption();
            ChronicleOption1 = new ChronicleOption();
            RandomOption0 = new RandomOption();
            RandomOption1 = new RandomOption();
            RandomOption2 = new RandomOption();
            RandomOptionChange = new RandomOption();
            Init();
        }

        public byte ItemKind { get; set; }

        public int ItemId { get; set; }

        public int Value { get; set; }

        public byte Attr { get; set; }

        public ushort Durability { get; set; }

        public byte SealFlag { get; set; }

        public int EnchantCardId { get; set; }

        public byte EnchantUpgradeCount { get; set; }

        public byte AmplifyType { get; set; }

        public ushort AmplifyValue { get; set; }

        public int Marker16 { get; set; }

        public ChronicleOption ChronicleOption0 { get; }

        public ChronicleOption ChronicleOption1 { get; }

        public int ExpireTime { get; set; }

        public byte EmblemSocketCount { get; set; }

        public int EmblemId1 { get; set; }

        public int EmblemId2 { get; set; }

        public ushort Rune { get; set; }

        public RandomOption RandomOption0 { get; }

        public RandomOption RandomOption1 { get; }

        public RandomOption RandomOption2 { get; }

        public byte RandomOptionState { get; set; }

        public byte RandomOptionChangedIndex { get; set; }

        public byte RandomOptionChangeState { get; set; }

        public RandomOption RandomOptionChange { get; }

        public byte GenuineUpgrade { get; set; }

        public byte EmancipateEquipmentLevel { get; set; }

        public byte TradeRestriction { get; set; }

        public ushort TailUnknown0 { get; set; }

        public byte TailUnknown1 { get; set; }

        public byte TailUnknown2 { get; set; }

        public byte TailUnknown3 { get; set; }

        public byte RemainUseCount { get; set; }

        public byte SortLockFlag { get; set; }

        public byte EquipmentLockId { get; set; }

        public bool IsEmpty => ItemKind == KindUnknown && ItemId == 0;

        public int InstanceValue
        {
            get => Value;
            set => Value = value;
        }

        public int Count
        {
            get => Value;
            set => Value = value;
        }

        public int Uid
        {
            get => Value;
            set => Value = value;
        }

        public int AvatarUid
        {
            get => Value;
            set => Value = value;
        }

        public int CreatureUid
        {
            get => Value;
            set => Value = value;
        }

        public ushort AbilityNo
        {
            get => Durability;
            set => Durability = value;
        }

        public byte Upgrade
        {
            get => (byte)(Attr & 0x1F);
            set => Attr = (byte)((Attr & 0xE0) | (value & 0x1F));
        }

        public byte ReSealCount
        {
            get => (byte)(Attr >> 5);
            set => Attr = (byte)((Attr & 0x1F) | ((value & 0x07) << 5));
        }

        public byte StackTradeCount
        {
            get => (byte)(Attr >> 5);
            set => Attr = (byte)((Attr & 0x1F) | ((value & 0x07) << 5));
        }

        public byte ChronicleOptionCount
        {
            get
            {
                byte count = 0;
                if (!ChronicleOption0.IsEmpty)
                    count++;
                if (!ChronicleOption1.IsEmpty)
                    count++;
                return count;
            }
        }

        public IReadOnlyList<ChronicleOption> ChronicleOptions
        {
            get
            {
                var result = new List<ChronicleOption>(2);
                if (!ChronicleOption0.IsEmpty)
                    result.Add(ChronicleOption0.Copy());
                if (!ChronicleOption1.IsEmpty)
                    result.Add(ChronicleOption1.Copy());
                return result;
            }
        }

        public byte RandomOptionCount
        {
            get
            {
                byte count = 0;
                if (!RandomOption0.IsEmpty)
                    count++;
                if (!RandomOption1.IsEmpty)
                    count++;
                if (!RandomOption2.IsEmpty)
                    count++;
                return count;
            }
        }

        public IReadOnlyList<RandomOption> RandomOptions
        {
            get
            {
                var result = new List<RandomOption>(3);
                if (!RandomOption0.IsEmpty)
                    result.Add(RandomOption0.Copy());
                if (!RandomOption1.IsEmpty)
                    result.Add(RandomOption1.Copy());
                if (!RandomOption2.IsEmpty)
                    result.Add(RandomOption2.Copy());
                return result;
            }
        }

        public void Init()
        {
            ItemKind = 0;
            ItemId = 0;
            Value = 0;
            Attr = 0;
            Durability = 0;
            SealFlag = 0;
            EnchantCardId = 0;
            EnchantUpgradeCount = 0;
            AmplifyType = 0;
            AmplifyValue = 0;
            Marker16 = Marker16Default;
            ChronicleOption0.Clear();
            ChronicleOption1.Clear();
            ExpireTime = 0;
            EmblemSocketCount = 0;
            EmblemId1 = 0;
            EmblemId2 = 0;
            Rune = 0;
            RandomOption0.Clear();
            RandomOption1.Clear();
            RandomOption2.Clear();
            RandomOptionState = 0;
            RandomOptionChangedIndex = RandomOptionChangedIndexDefault;
            RandomOptionChangeState = 0;
            RandomOptionChange.Clear();
            GenuineUpgrade = 0;
            EmancipateEquipmentLevel = 0;
            TradeRestriction = 0;
            TailUnknown0 = 0;
            TailUnknown1 = 0;
            TailUnknown2 = 0;
            TailUnknown3 = 0;
            RemainUseCount = 0;
            SortLockFlag = 0;
            EquipmentLockId = 0;
        }

        public static ItemCore Create(byte itemKind, int itemId)
        {
            return new ItemCore
            {
                ItemKind = itemKind,
                ItemId = itemId,
            };
        }

        public void CopyFrom(ItemCore source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            ItemKind = source.ItemKind;
            ItemId = source.ItemId;
            Value = source.Value;
            Attr = source.Attr;
            Durability = source.Durability;
            SealFlag = source.SealFlag;
            EnchantCardId = source.EnchantCardId;
            EnchantUpgradeCount = source.EnchantUpgradeCount;
            AmplifyType = source.AmplifyType;
            AmplifyValue = source.AmplifyValue;
            Marker16 = source.Marker16;
            ChronicleOption0.CopyFrom(source.ChronicleOption0);
            ChronicleOption1.CopyFrom(source.ChronicleOption1);
            ExpireTime = source.ExpireTime;
            EmblemSocketCount = source.EmblemSocketCount;
            EmblemId1 = source.EmblemId1;
            EmblemId2 = source.EmblemId2;
            Rune = source.Rune;
            RandomOption0.CopyFrom(source.RandomOption0);
            RandomOption1.CopyFrom(source.RandomOption1);
            RandomOption2.CopyFrom(source.RandomOption2);
            RandomOptionState = source.RandomOptionState;
            RandomOptionChangedIndex = source.RandomOptionChangedIndex;
            RandomOptionChangeState = source.RandomOptionChangeState;
            RandomOptionChange.CopyFrom(source.RandomOptionChange);
            GenuineUpgrade = source.GenuineUpgrade;
            EmancipateEquipmentLevel = source.EmancipateEquipmentLevel;
            TradeRestriction = source.TradeRestriction;
            TailUnknown0 = source.TailUnknown0;
            TailUnknown1 = source.TailUnknown1;
            TailUnknown2 = source.TailUnknown2;
            TailUnknown3 = source.TailUnknown3;
            RemainUseCount = source.RemainUseCount;
            SortLockFlag = source.SortLockFlag;
            EquipmentLockId = source.EquipmentLockId;
        }

        public ItemCore Copy()
        {
            var copy = new ItemCore();
            copy.CopyFrom(this);
            return copy;
        }

        public void SetChronicleOptions(IReadOnlyList<ChronicleOption> options)
        {
            ChronicleOption0.Clear();
            ChronicleOption1.Clear();

            if (options == null)
                return;

            int targetIndex = 0;
            for (int i = 0; i < options.Count && targetIndex < 2; i++)
            {
                var option = options[i];
                if (option == null || option.IsEmpty)
                    continue;

                GetChronicleOptionSlot(targetIndex).CopyFrom(option);
                targetIndex++;
            }
        }

        public ChronicleOption GetChronicleOptionSlot(int index)
        {
            switch (index)
            {
                case 0:
                    return ChronicleOption0;
                case 1:
                    return ChronicleOption1;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, "异界气息槽位范围是 0-1。");
            }
        }

        public void SetRandomOptions(IReadOnlyList<RandomOption> options)
        {
            RandomOption0.Clear();
            RandomOption1.Clear();
            RandomOption2.Clear();

            if (options == null)
                return;

            int targetIndex = 0;
            for (int i = 0; i < options.Count && targetIndex < 3; i++)
            {
                var option = options[i];
                if (option == null || option.IsEmpty)
                    continue;

                GetRandomOptionSlot(targetIndex).CopyFrom(option);
                targetIndex++;
            }
        }

        public RandomOption GetRandomOptionSlot(int index)
        {
            switch (index)
            {
                case 0:
                    return RandomOption0;
                case 1:
                    return RandomOption1;
                case 2:
                    return RandomOption2;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, "魔法封印槽位范围是 0-2。");
            }
        }

        public bool IsCreatureItem()
        {
            return ItemKind == KindCreature
                || ItemKind == KindCreatureEquipment
                || ItemKind == KindCreatureConsumable;
        }

        public bool IsEquipmentItem()
        {
            return ItemKind == KindEquipment
                || ItemKind == KindCreature
                || ItemKind == KindCreatureEquipment
                || ItemKind == KindAvatar;
        }

        public bool IsAvatarItem()
        {
            return ItemKind == KindAvatar;
        }

        public byte[] ToBytes()
        {
            var buffer = new byte[Size];
            buffer[ItemKindOffset] = ItemKind;
            WriteInt32(buffer, ItemIdOffset, ItemId);
            WriteInt32(buffer, ValueOffset, Value);
            buffer[AttrOffset] = Attr;
            WriteUInt16(buffer, DurabilityOffset, Durability);
            buffer[SealFlagOffset] = SealFlag;
            WriteInt32(buffer, EnchantCardIdOffset, EnchantCardId);
            buffer[EnchantUpgradeCountOffset] = EnchantUpgradeCount;
            buffer[AmplifyTypeOffset] = AmplifyType;
            WriteUInt16(buffer, AmplifyValueOffset, AmplifyValue);
            WriteInt32(buffer, Marker16Offset, Marker16);
            WriteChronicleOption(buffer, ChronicleOption0Offset, ChronicleOption0);
            WriteChronicleOption(buffer, ChronicleOption1Offset, ChronicleOption1);
            WriteInt32(buffer, ExpireTimeOffset, ExpireTime);
            buffer[EmblemSocketCountOffset] = EmblemSocketCount;
            WriteInt32(buffer, EmblemId1Offset, EmblemId1);
            WriteInt32(buffer, EmblemId2Offset, EmblemId2);
            WriteUInt16(buffer, RuneOffset, Rune);
            WriteRandomOption(buffer, RandomOption0Offset, RandomOption0);
            WriteRandomOption(buffer, RandomOption1Offset, RandomOption1);
            WriteRandomOption(buffer, RandomOption2Offset, RandomOption2);
            buffer[RandomOptionStateOffset] = RandomOptionState;
            buffer[RandomOptionChangedIndexOffset] = RandomOptionChangedIndex;
            buffer[RandomOptionChangeStateOffset] = RandomOptionChangeState;
            buffer[RandomOptionChangeTypeOffset] = RandomOptionChange.Type;
            buffer[RandomOptionChangeValue1Offset] = RandomOptionChange.Value1;
            buffer[RandomOptionChangeValue2Offset] = RandomOptionChange.Value2;
            buffer[GenuineUpgradeOffset] = GenuineUpgrade;
            buffer[EmancipateEquipmentLevelOffset] = EmancipateEquipmentLevel;
            buffer[TradeRestrictionOffset] = TradeRestriction;
            WriteUInt16(buffer, TailUnknown0Offset, TailUnknown0);
            buffer[TailUnknown1Offset] = TailUnknown1;
            buffer[TailUnknown2Offset] = TailUnknown2;
            buffer[TailUnknown3Offset] = TailUnknown3;
            buffer[RemainUseCountOffset] = RemainUseCount;
            buffer[SortLockFlagOffset] = SortLockFlag;
            buffer[EquipmentLockIdOffset] = EquipmentLockId;
            return buffer;
        }

        public static ItemCore FromBytes(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return FromBytes(data.AsSpan());
        }

        public static ItemCore FromBytes(ReadOnlySpan<byte> data)
        {
            if (data.Length < Size)
                throw new ArgumentException("ItemCore 字节长度不足。", nameof(data));

            var item = new ItemCore
            {
                ItemKind = data[ItemKindOffset],
                ItemId = ReadInt32(data, ItemIdOffset),
                Value = ReadInt32(data, ValueOffset),
                Attr = data[AttrOffset],
                Durability = ReadUInt16(data, DurabilityOffset),
                SealFlag = data[SealFlagOffset],
                EnchantCardId = ReadInt32(data, EnchantCardIdOffset),
                EnchantUpgradeCount = data[EnchantUpgradeCountOffset],
                AmplifyType = data[AmplifyTypeOffset],
                AmplifyValue = ReadUInt16(data, AmplifyValueOffset),
                Marker16 = ReadInt32(data, Marker16Offset),
                ExpireTime = ReadInt32(data, ExpireTimeOffset),
                EmblemSocketCount = data[EmblemSocketCountOffset],
                EmblemId1 = ReadInt32(data, EmblemId1Offset),
                EmblemId2 = ReadInt32(data, EmblemId2Offset),
                Rune = ReadUInt16(data, RuneOffset),
                RandomOptionState = data[RandomOptionStateOffset],
                RandomOptionChangedIndex = data[RandomOptionChangedIndexOffset],
                RandomOptionChangeState = data[RandomOptionChangeStateOffset],
                GenuineUpgrade = data[GenuineUpgradeOffset],
                EmancipateEquipmentLevel = data[EmancipateEquipmentLevelOffset],
                TradeRestriction = data[TradeRestrictionOffset],
                TailUnknown0 = ReadUInt16(data, TailUnknown0Offset),
                TailUnknown1 = data[TailUnknown1Offset],
                TailUnknown2 = data[TailUnknown2Offset],
                TailUnknown3 = data[TailUnknown3Offset],
                RemainUseCount = data[RemainUseCountOffset],
                SortLockFlag = data[SortLockFlagOffset],
                EquipmentLockId = data[EquipmentLockIdOffset],
            };

            ReadChronicleOption(data, ChronicleOption0Offset, item.ChronicleOption0);
            ReadChronicleOption(data, ChronicleOption1Offset, item.ChronicleOption1);
            ReadRandomOption(data, RandomOption0Offset, item.RandomOption0);
            ReadRandomOption(data, RandomOption1Offset, item.RandomOption1);
            ReadRandomOption(data, RandomOption2Offset, item.RandomOption2);
            item.RandomOptionChange.Type = data[RandomOptionChangeTypeOffset];
            item.RandomOptionChange.Value1 = data[RandomOptionChangeValue1Offset];
            item.RandomOptionChange.Value2 = data[RandomOptionChangeValue2Offset];
            return item;
        }

        private static void ReadChronicleOption(ReadOnlySpan<byte> data, int offset, ChronicleOption option)
        {
            option.OptionId = ReadInt32(data, offset);
            option.CharacJob = data[offset + 4];
            option.FirstGrowType = data[offset + 5];
            option.EquipmentType = data[offset + 6];
            option.OptionNo = data[offset + 7];
        }

        private static void WriteChronicleOption(byte[] buffer, int offset, ChronicleOption option)
        {
            WriteInt32(buffer, offset, option.OptionId);
            buffer[offset + 4] = option.CharacJob;
            buffer[offset + 5] = option.FirstGrowType;
            buffer[offset + 6] = option.EquipmentType;
            buffer[offset + 7] = option.OptionNo;
        }

        private static void ReadRandomOption(ReadOnlySpan<byte> data, int offset, RandomOption option)
        {
            option.Type = data[offset];
            option.Value1 = data[offset + 1];
            option.Value2 = data[offset + 2];
        }

        private static void WriteRandomOption(byte[] buffer, int offset, RandomOption option)
        {
            buffer[offset] = option.Type;
            buffer[offset + 1] = option.Value1;
            buffer[offset + 2] = option.Value2;
        }

        private static int ReadInt32(ReadOnlySpan<byte> data, int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), value);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), value);
        }
    }
}
