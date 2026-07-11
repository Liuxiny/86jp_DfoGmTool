using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.SelectCharacter
{
    public sealed class SkillInfoEntrySnapshot
    {
        public byte Slot { get; set; }

        public ushort SkillId { get; set; }

        public byte Level { get; set; }

        public List<byte> ExtraValues { get; } = new List<byte>();
    }

    public sealed class SkillInfoPageSnapshot
    {
        public ushort HeaderValue { get; set; }

        public List<SkillInfoEntrySnapshot> Entries { get; } = new List<SkillInfoEntrySnapshot>();
    }

    public sealed class SkillInfoSnapshot
    {
        public List<SkillInfoPageSnapshot> Pages { get; } = new List<SkillInfoPageSnapshot>();

        public ushort Tail0 { get; set; }

        public ushort Tail1 { get; set; }

        public bool HasTailValues { get; set; }
    }
}
