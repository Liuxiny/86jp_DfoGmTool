using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace GmPvfLib
{
    
    
    
    public static class PvfDecryptor
    {
        private static readonly byte[] ZlibHeader = { 0x78, 0x9C };
        private const uint AdlerBase = 65521;

        #region Zlib 压缩/解压

        public static byte[] ZlibCompress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            using (var output = new MemoryStream(data.Length / 2))
            {
                output.Write(ZlibHeader, 0, ZlibHeader.Length);

                byte[] deflated;
                using (var tmp = new MemoryStream())
                {
                    using (var deflater = new DeflateStream(tmp, CompressionMode.Compress, true))
                        deflater.Write(data, 0, data.Length);
                    deflated = tmp.ToArray();
                }
                output.Write(deflated, 0, deflated.Length);

                
                byte[] checksum = BitConverter.GetBytes(Adler32(data));
                if (BitConverter.IsLittleEndian) Array.Reverse(checksum);
                output.Write(checksum, 0, checksum.Length);

                return output.ToArray();
            }
        }

        public static byte[] ZlibDecompress(byte[] compressed)
        {
            if (compressed == null || compressed.Length < 6)
                throw new ArgumentException("压缩数据长度不足");
            if (compressed[0] != 0x78)
                throw new ArgumentException("无效的 Zlib 头");

            uint storedChecksum = ReadBigEndianUInt32(compressed, compressed.Length - 4);
            int payloadLen = compressed.Length - 6; 

            byte[] decompressed;
            using (var input = new MemoryStream(compressed, 2, payloadLen))
            using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(payloadLen * 4))
            {
                inflater.CopyTo(output);
                decompressed = output.ToArray();
            }

            uint calcChecksum = Adler32(decompressed);
            if (storedChecksum != calcChecksum)
                throw new InvalidDataException(
                    $"Adler32 校验失败: 期望 0x{storedChecksum:X8}, 实际 0x{calcChecksum:X8}");

            return decompressed;
        }

        #endregion

        #region PVF 专用解密

        
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecryptGuard(byte[] buf)
        {
            if (buf == null || buf.Length < 28) return;
            for (int i = 24; i < 28; i++)
                buf[i] ^= 0x55;
        }

        
        
        
        public static int Decrypt(string key, byte[] buf)
            => DecryptCore(key, buf, 0x269EC3);

        
        
        
        public static int Decrypt2(string key, byte[] buf)
            => DecryptCore(key, buf, 0x269EC9);

        private static int DecryptCore(string key, byte[] buf, int magic)
        {
            if (string.IsNullOrEmpty(key) || buf == null || buf.Length == 0)
                return 0;

            byte[] k = Encoding.ASCII.GetBytes(key);
            if (k.Length < 4) return 0;

            int len = buf.Length;
            int tail = len;
            int seed = 0x76826701 * k[0] + 0x1C1 * (k[3] + 0x1C1 * (k[2] + 0x1C1 * k[1]));

            
            if (len >= 4)
            {
                int quadCount = len >> 2;
                tail = len - (quadCount << 2);

                for (int i = 0; i < quadCount; i++)
                {
                    int t1 = 0x343FD * seed + magic;
                    seed = 0x343FD * t1 + magic;

                    uint xorKey = (uint)(((seed >> 16) & 0xFFFF) + (t1 & 0xFFFF0000));
                    int off = i << 2;

                    uint data = BitConverter.ToUInt32(buf, off) ^ xorKey;
                    Buffer.BlockCopy(BitConverter.GetBytes(data), 0, buf, off, 4);
                }
            }

            
            if (tail > 0)
            {
                int t1 = 0x343FD * seed + magic;
                int t2 = 0x343FD * t1 + magic;
                uint finalKey = (uint)((t1 & 0xFFFF0000) + ((t2 >> 16) & 0xFFFF));

                byte[] keyBytes = BitConverter.GetBytes(finalKey);
                int start = len - tail;
                for (int i = 0; i < tail; i++)
                    buf[start + i] ^= keyBytes[i];
            }

            return tail;
        }

        #endregion

        #region 内部工具

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte v in data)
            {
                a = (a + v) % AdlerBase;
                b = (b + a) % AdlerBase;
            }
            return (b << 16) | a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadBigEndianUInt32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) |
                          (data[offset + 1] << 16) |
                          (data[offset + 2] << 8) |
                          data[offset + 3]);
        }

        #endregion
    }
}
