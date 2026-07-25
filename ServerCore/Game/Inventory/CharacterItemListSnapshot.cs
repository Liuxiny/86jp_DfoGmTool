using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public enum InventoryListType : byte
    {
        Main = 0,
        Avatar = 1,
        PersonalCargo = 2,
        Equipment = 3,
        Pet = 7,
        AccountCargo = 12,
        TitleBookGeneral = 19,
        TitleBookSpecific = 20,
        TitleBookPvp = 21,
        TitleBookDespair = 22,
        TitleBookEvent = 23,
        QuickSlot = 29,
        KnightShieldEquipped = 33,
        KnightShieldCatalog = 34,
    }

    // ITEM_LIST/UPDATE_ITEM_LIST 的普通 0x54 entry 协议 DTO。
    // 业务逻辑不要直接读写 PrefixData0E/MiddleData1A/TailData2F 等协议字段；
    // 新业务应通过 ItemRecord + ItemExtraView 表达语义，发包前再映射到此 DTO。
    public sealed class CommonInventoryItem
    {
        public short SlotIndex { get; set; }

        public int ItemTemplateId { get; set; }

        public int CountOrInstanceValue { get; set; }

        public byte ExtData0 { get; set; }

        public ushort Durability { get; set; }

        public byte SealFlag { get; set; }

        public byte[] PrefixData0E { get; set; } = new byte[8];

        public int Marker16 { get; set; }

        public byte[] MiddleData1A { get; set; } = new byte[17];

        public int ExpireTime { get; set; }

        public byte[] TailData2F { get; set; } = new byte[37];

        public byte[] JewelSocket { get; set; } = new byte[30];

        public byte EquipmentLockId { get; set; }
    }

    // ITEM_LIST/UPDATE_ITEM_LIST 的时装协议 DTO，只用于初始化和刷新包构造边界。
    // 业务逻辑不要把 Reserved0/Reserved1/Reserved2 当作业务模型直接操作。
    public sealed class AvatarInventoryItem
    {
        public short SlotIndex { get; set; }

        public int AvatarItemId { get; set; }

        public int ExpireTime { get; set; }

        public byte[] Reserved0 { get; set; } = new byte[5];

        public byte OptionValue { get; set; }

        public byte[] Reserved1 { get; set; } = new byte[71];

        public int UnknownFixed30 { get; set; }

        public byte[] Reserved2 { get; set; } = new byte[30];

        public ushort UnknownFixed4 { get; set; }

        public byte[] TailData { get; set; } = new byte[7];
    }

    // ITEM_LIST/UPDATE_ITEM_LIST 的宠物协议 DTO，只用于初始化和刷新包构造边界。
    // 宠物业务状态应从宠物实例/详情模型读取，再在发包前映射到此 DTO。
    public sealed class PetInventoryItem
    {
        public short SlotIndex { get; set; }

        public int CreatureItemId { get; set; }

        public int CreatureSerialOrHandle { get; set; }

        // 宠物用品槽位中，服务端将堆叠数量镜像到三个字段；这里保留归一化后的业务数量。
        public int StackCount { get; set; } = 1;

        public int ExpireTime { get; set; }

        public byte[] TailData0A { get; set; } = new byte[74];
    }

    public sealed class AccountCargoStateSnapshot
    {
        public ushort SelectionKey { get; set; }

        public ushort ItemCount { get; set; }

        public int Value32 { get; set; }
    }

    // 选角/进图 ITEM_LIST 的协议快照，不是运行时物品业务模型。
    // handler 不应长期依赖它反查业务状态；需要刷新时应从业务结果或记录重新映射。
    public sealed class CharacterItemListSnapshot
    {
        public ushort MainListParam16 { get; set; }

        public ushort AvatarListParam16 { get; set; }

        public ushort PersonalCargoListParam16 { get; set; }

        public List<CommonInventoryItem> MainItems { get; } = new List<CommonInventoryItem>();

        public List<AvatarInventoryItem> AvatarItems { get; } = new List<AvatarInventoryItem>();

        public List<AvatarInventoryItem> EquipmentItems { get; } = new List<AvatarInventoryItem>();

        public List<CommonInventoryItem> PersonalCargoItems { get; } = new List<CommonInventoryItem>();

        public List<PetInventoryItem> PetItems { get; } = new List<PetInventoryItem>();

        public List<CommonInventoryItem> AccountCargoItems { get; } = new List<CommonInventoryItem>();

        public AccountCargoStateSnapshot AccountCargoState { get; set; } = new AccountCargoStateSnapshot();

        public static byte[] Slice(byte[] source, int offset, int length)
        {
            var buffer = new byte[length];
            Array.Copy(source, offset, buffer, 0, length);
            return buffer;
        }
    }

    public sealed class SortItemLockEntry
    {
        public InventoryListType ListType { get; set; }

        public short SlotIndex { get; set; }

        public byte State { get; set; } = 1;
    }
}
