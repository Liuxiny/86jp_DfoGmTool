using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.SelectCharacter
{
    public sealed class CreatureItemEntrySnapshot
    {
        public int CreatureKey { get; set; }

        public byte Field04 { get; set; }

        public byte ModeFlag { get; set; }

        public int ProgressValue32 { get; set; }

        public byte Mode1Field0A { get; set; }

        public byte Mode1Field0B { get; set; }

        public byte FieldAfterValue32 { get; set; }

        public byte[] CreatureTextBytes { get; set; } = new byte[0];

        public byte TailFlag { get; set; }

        public string ExtraJson { get; set; } = "{}";
    }

    public sealed class CreatureItemListSnapshot
    {
        public List<CreatureItemEntrySnapshot> Entries { get; } = new List<CreatureItemEntrySnapshot>();
    }
}
