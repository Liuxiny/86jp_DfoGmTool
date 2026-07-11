using DfoGmTool.ServerCore.Game.Inventory;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.TitleBook
{
    public sealed class TitleBookInventoryItem
    {
        public int Category { get; set; }

        public ushort BookIndex { get; set; }

        public ushort Slot { get; set; }

        public int ItemId { get; set; } = -1;

        public int Value { get; set; }

        public byte Attr { get; set; }

        public ushort Durability { get; set; }

        public byte SealFlag { get; set; }

        public int EnchantIndex { get; set; }

        public byte EnchantUpgradeCount { get; set; }

        public byte AmplifyType { get; set; }

        public ushort AmplifyValue { get; set; }

        public int Marker16 { get; set; }

        public TitleBookChronicleData Chronicle { get; set; } = new TitleBookChronicleData();

        public int ExpireTime { get; set; }

        public byte[] TailData { get; set; } = new byte[37];

        public byte EquipmentLockId { get; set; }

        public bool IsEmpty => ItemId <= 0;
    }

    public sealed class TitleBookChronicleData
    {
        public byte Count { get; set; }

        public List<TitleBookChronicleOption> Options { get; } = new List<TitleBookChronicleOption>();
    }

    public sealed class TitleBookChronicleOption
    {
        public int OptionId { get; set; }

        public byte CharacJob { get; set; }

        public byte FirstGrowType { get; set; }

        public byte EquipmentType { get; set; }

        public byte OptionNo { get; set; }
    }

    public sealed class TitleBookListEntrySnapshot
    {
        public ushort SlotIndex { get; set; }

        public int ItemId { get; set; }

        public int Value { get; set; }

        public byte Attr { get; set; }

        public ushort Durability { get; set; }

        public byte SealFlag { get; set; }

        public int EnchantIndex { get; set; }

        public byte EnchantUpgradeCount { get; set; }

        public byte AmplifyType { get; set; }

        public ushort AmplifyValue { get; set; }
    }

    public sealed class TitleBookCategorySnapshot
    {
        public byte InfoType { get; set; }

        public ushort OwnerId16 { get; set; }

        public int Category { get; set; }

        public List<TitleBookListEntrySnapshot> Entries { get; } = new List<TitleBookListEntrySnapshot>();
    }

    public sealed class TitleBookSlotDefinition
    {
        public int Category { get; set; }

        public int Index { get; set; }

        public int SlotType { get; set; } = -1;

        public int QuestId { get; set; } = -1;

        public List<int> AllowedTitleItemIds { get; } = new List<int>();

        public bool IsOpen => SlotType >= 0;

        public bool AllowsItem(int itemId)
        {
            if (!IsOpen)
                return false;

            if (AllowedTitleItemIds.Count == 0)
                return QuestId == -1;

            return AllowedTitleItemIds.Contains(itemId) || AllowedTitleItemIds.Contains(-1);
        }
    }

    public sealed class TitleBookMutationResult
    {
        public bool Success { get; set; }

        public byte ErrorCode { get; set; } = 0x0A;

        public InventoryListType ItemSpace { get; set; }

        public short SlotIndex { get; set; }

        public int Category { get; set; }

        public int BookIndex { get; set; }

        public int ItemId { get; set; }

        public bool InventoryChanged { get; set; }

        public bool EquipmentChanged { get; set; }

        public bool ItemLockChanged { get; set; }
    }

    public sealed class AchievementTriggerResult
    {
        public bool Success { get; set; }

        public byte ErrorCode { get; set; } = 0x0A;

        public int QuestId { get; set; }

        public ushort Remain1 { get; set; }

        public ushort Remain2 { get; set; }

        public ushort Remain3 { get; set; }

        public ushort TailOrState { get; set; }

        public bool Completed { get; set; }

        public int Category { get; set; } = -1;

        public int BookIndex { get; set; } = -1;

        public int TitleItemId { get; set; } = -1;
    }

    public sealed class TitleQuestDefinition
    {
        public int QuestId { get; set; }

        public ushort CheckCount { get; set; } = 1;

        public int RewardTitleItemId { get; set; } = -1;
    }
}
