using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class AvatarSocketDataCodec
    {
        public const int Length = 30;

        public static byte[] Normalize(byte[] source)
        {
            var data = new byte[Length];
            if (source != null && source.Length > 0)
                Buffer.BlockCopy(source, 0, data, 0, Math.Min(source.Length, Length));

            return NormalizeCanonicalSocketTypes(LooksLikeLegacyShifted(data) ? ConvertLegacyShiftedToCanonical(data) : data);
        }

        public static ushort NormalizeSocketType(ushort socketType)
        {
            return socketType == 0x00EF ? (ushort)0xFFEF : socketType;
        }

        private static byte[] NormalizeCanonicalSocketTypes(byte[] data)
        {
            for (var i = 0; i < 5; i++)
            {
                var offset = i * 6;
                var socketType = BitConverter.ToUInt16(data, offset);
                var normalized = NormalizeSocketType(socketType);
                if (normalized != socketType)
                    BitConverter.GetBytes(normalized).CopyTo(data, offset);
            }

            return data;
        }

        private static bool LooksLikeLegacyShifted(byte[] data)
        {
            for (var i = 0; i < 5; i++)
            {
                var offset = i * 6;
                if (data[offset] == 0 && IsKnownSocketType(data[offset + 1]))
                    return true;
            }

            return false;
        }

        private static byte[] ConvertLegacyShiftedToCanonical(byte[] legacy)
        {
            var data = new byte[Length];
            for (var i = 0; i < 5; i++)
            {
                var offset = i * 6;
                if (legacy[offset] == 0 && IsKnownSocketType(legacy[offset + 1]))
                {
                    data[offset] = legacy[offset + 1];
                    data[offset + 1] = legacy[offset + 2];
                    Buffer.BlockCopy(legacy, offset + 3, data, offset + 2, 3);
                }
                else
                {
                    Buffer.BlockCopy(legacy, offset, data, offset, 6);
                }
            }

            return data;
        }

        private static bool IsKnownSocketType(byte type)
        {
            return type == 0x01
                || type == 0x02
                || type == 0x04
                || type == 0x08
                || type == 0x10
                || type == 0xEF;
        }
    }
}
