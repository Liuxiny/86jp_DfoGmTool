using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.SelectCharacter
{
    public sealed class UserInfoAdditionSnapshot
    {
        
        public uint CharacExp { get; set; }

        
        public uint StatHpMax { get; set; }
        public uint StatMpMax { get; set; }
        public short StatPhysicalAttack { get; set; }
        public short StatPhysicalDefense { get; set; }
        public short StatMagicalAttack { get; set; }
        public short StatMagicalDefense { get; set; }
        public short StatFireResistance { get; set; }
        public short StatWaterResistance { get; set; }
        public short StatDarkResistance { get; set; }
        public short StatLightResistance { get; set; }
        
        
        public uint StatInventoryLimit { get; set; }
        public ushort StatHpRegenSpeed { get; set; }
        public ushort StatMpRegenSpeed { get; set; }
        public uint StatMoveSpeed { get; set; }
        public ushort StatAttackSpeed { get; set; }
        public ushort StatCastSpeed { get; set; }
        public ushort StatHitRecovery { get; set; }
        public ushort StatJumpPower { get; set; }
        public uint StatWeight { get; set; }
        public byte StatLevel { get; set; }

        
        public byte ExEquipSlotStat { get; set; }

        
        public List<EquippedEntrySnapshot> EquippedEntries { get; } = new List<EquippedEntrySnapshot>();
        // subtype1 装备列表末尾的独立字段，不属于 InvenItem；0 会清空克隆称号动画。
        public uint CloneTitleItemId { get; set; }

        
        public uint NameTagItemId { get; set; }
        public uint NameTagExpireTime { get; set; }

        
        public byte SkillTreeIndex { get; set; }

        
        public byte EquippedCreatureLevel { get; set; }

        
        public List<DimensionEntrySnapshot> Dimensions { get; } = new List<DimensionEntrySnapshot>();
        public byte DimFlag1 { get; set; }
        public byte DimFlag2 { get; set; }
        public byte DimFlag3 { get; set; }
        public byte DimFlag4 { get; set; }

        
        public List<PvpResultEntrySnapshot> PvpResults { get; } = new List<PvpResultEntrySnapshot>();

        
        public byte ManageLevel { get; set; }
        public List<uint> AbuseValues { get; } = new List<uint>();
        public byte FlagByte { get; set; }
        public uint GuildPowerWar { get; set; }
        public uint ServerTimestamp { get; set; }
        public ushort QuestShopCount { get; set; }
        public uint Progress1 { get; set; }
        public uint Progress2 { get; set; }
    }

    public sealed class EquippedEntrySnapshot
    {
        public int Slot { get; set; }
        public int ItemId { get; set; }
        
        public byte[] RawEntry { get; set; }
        
        public Game.Inventory.InvenItem Item { get; set; }
    }

    public sealed class DimensionEntrySnapshot
    {
        public uint Key { get; set; }
        public byte Val1 { get; set; }
        public byte Val2 { get; set; }
    }

    public sealed class PvpResultEntrySnapshot
    {
        public uint Value32 { get; set; }
        public ushort Value16A { get; set; }
        public ushort Value16B { get; set; }
    }
}
