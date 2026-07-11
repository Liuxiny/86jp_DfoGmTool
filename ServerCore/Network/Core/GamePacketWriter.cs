using System;
using System.Collections.Generic;
using System.Text;

namespace DfoGmTool.ServerCore.Network
{
    public sealed class GamePacketWriter
    {
        private readonly List<byte> _buffer = new List<byte>();

        public void WriteByte(byte value)
        {
            _buffer.Add(value);
        }

        public void WriteBytes(byte[] values)
        {
            if (values == null || values.Length == 0)
                return;

            _buffer.AddRange(values);
        }

        public void WriteInt16(short value)
        {
            _buffer.AddRange(BitConverter.GetBytes(value));
        }

        public void WriteUInt16(ushort value)
        {
            _buffer.AddRange(BitConverter.GetBytes(value));
        }

        public void WriteInt32(int value)
        {
            _buffer.AddRange(BitConverter.GetBytes(value));
        }

        public void WriteUInt32(uint value)
        {
            _buffer.AddRange(BitConverter.GetBytes(value));
        }

        public void WriteZeroBytes(int count)
        {
            if (count <= 0)
                return;

            _buffer.AddRange(new byte[count]);
        }

        public void WriteUtf8Dstr(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }

        public void WriteDstr(byte[] rawNameBytes)
        {
            var bytes = rawNameBytes ?? System.Array.Empty<byte>();
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }

        public void WriteAsciiDstr(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }

        // 直接写已经是原始字节的 dstr(= int32 len + 原始字节)。组队队名等场景使用。
        public void WriteRawDstr(byte[] rawBytes)
        {
            var bytes = rawBytes ?? System.Array.Empty<byte>();
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }

        public byte[] ToArray()
        {
            return _buffer.ToArray();
        }
    }
}
