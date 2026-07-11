using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GmPvfLib
{
    public static class ByteArrayExtensions
    {
        
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] Slice(this byte[] source, int offset, int length)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if ((uint)offset > (uint)source.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if ((uint)length > (uint)(source.Length - offset)) throw new ArgumentOutOfRangeException(nameof(length));

            var result = new byte[length];
            Buffer.BlockCopy(source, offset, result, 0, length);
            return result;
        }

        
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ToStruct<T>(this byte[] data) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            if (data.Length < size)
                throw new ArgumentException($"数据长度不足: 需要 {size} 字节, 实际 {data.Length} 字节");

            unsafe
            {
                fixed (byte* p = data)
                {
                    return Marshal.PtrToStructure<T>((IntPtr)p);
                }
            }
        }
    }
}
