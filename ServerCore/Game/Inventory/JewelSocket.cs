using System;
using System.Buffers.Binary;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class JewelSocket
    {
        public const int SocketCount = 5;
        public const int EntrySize = 6;
        public const int Size = SocketCount * EntrySize;

        private readonly byte[] _data = new byte[Size];

        public int OpenCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < SocketCount; index++)
                {
                    if (GetSocketType(index) != 0)
                        count++;
                }

                return count;
            }
        }

        public JewelSocketSlot this[int index]
        {
            get => Get(index);
            set => Set(index, value);
        }

        public ushort GetSocketType(int index)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(GetOffset(index), 2));
        }

        public void SetSocketType(int index, ushort socketType)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                _data.AsSpan(GetOffset(index), 2),
                AvatarSocketDataCodec.NormalizeSocketType(socketType));
        }

        public int GetEmblemId(int index)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(GetOffset(index) + 2, 4));
        }

        public void SetEmblemId(int index, int emblemId)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan(GetOffset(index) + 2, 4), emblemId);
        }

        public JewelSocketSlot Get(int index)
        {
            return new JewelSocketSlot(GetSocketType(index), GetEmblemId(index));
        }

        public void Set(int index, ushort socketType, int emblemId)
        {
            SetSocketType(index, socketType);
            SetEmblemId(index, emblemId);
        }

        public void Set(int index, JewelSocketSlot socket)
        {
            Set(index, socket.SocketType, socket.EmblemId);
        }

        public void Clear(int index)
        {
            Set(index, 0, 0);
        }

        public void CopyFrom(JewelSocket source)
        {
            Array.Clear(_data, 0, _data.Length);
            if (source == null)
                return;

            var sourceData = source.ToBytes();
            Buffer.BlockCopy(sourceData, 0, _data, 0, Size);
        }

        public JewelSocket Copy()
        {
            var copy = new JewelSocket();
            copy.CopyFrom(this);
            return copy;
        }

        public byte[] ToBytes()
        {
            var result = new byte[Size];
            Buffer.BlockCopy(_data, 0, result, 0, Size);
            return result;
        }

        public static JewelSocket FromBytes(byte[] data)
        {
            var socket = new JewelSocket();
            var normalized = AvatarSocketDataCodec.Normalize(data);
            Buffer.BlockCopy(normalized, 0, socket._data, 0, Size);
            return socket;
        }

        private static int GetOffset(int index)
        {
            if (index < 0 || index >= SocketCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, "时装镶嵌孔位范围是 0-4。");

            return index * EntrySize;
        }
    }

    internal readonly struct JewelSocketSlot
    {
        public JewelSocketSlot(ushort socketType, int emblemId)
        {
            SocketType = socketType;
            EmblemId = emblemId;
        }

        public ushort SocketType { get; }

        public int EmblemId { get; }
    }
}
