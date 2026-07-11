using System.Runtime.InteropServices;

namespace GmPvfLib
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PvfHeader
    {
        public uint Signature;             
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Guid;
        public int FileCount;
        public int Padding;
        public int BodySize;
        public int GroupCount;
        public int HashTableSize;
        public int NameTableSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PvfFileItem
    {
        public int NameOffset;
        public int PathOffset;
        public int ChunkIndex;
        public int DataOffset;
        public int DataSize;
        public int DataType;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GrpiItem
    {
        public int CompressedSize;
        public int OriginalSize;
    }
}
