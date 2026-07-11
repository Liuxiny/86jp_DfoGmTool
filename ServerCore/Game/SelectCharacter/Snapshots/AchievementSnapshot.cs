using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.SelectCharacter
{
    public sealed class AchievementCompleteEntrySnapshot
    {
        public int AchievementId { get; set; }

        public ushort P1 { get; set; }

        public ushort P2 { get; set; }

        public ushort P3 { get; set; }

        public ushort P4 { get; set; }
    }

    public sealed class AchievementCompleteSnapshot
    {
        public List<AchievementCompleteEntrySnapshot> Entries { get; } = new List<AchievementCompleteEntrySnapshot>();
    }

    public sealed class AchievementListEntrySnapshot
    {
        public ushort AchievementId { get; set; }

        public int ValueA { get; set; }

        public int ValueB { get; set; }

        public byte CategoryByte { get; set; }

        public ushort LinkId { get; set; }

        public byte Flag0 { get; set; }

        public int ValueC { get; set; }

        public byte Flag1 { get; set; }

        public byte Flag2 { get; set; }

        public ushort TailValue { get; set; }
    }

    public sealed class AchievementListChunkSnapshot
    {
        public byte ModeByte { get; set; }

        public ushort OwnerId16 { get; set; }

        public int ChunkIndex { get; set; }

        public List<AchievementListEntrySnapshot> Entries { get; } = new List<AchievementListEntrySnapshot>();
    }
}
