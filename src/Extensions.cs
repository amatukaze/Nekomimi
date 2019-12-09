using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nekomimi
{
    internal static class Extensions
    {
        public static short ReadShort(this ReadOnlySpan<byte> span)
        {
            var result = Unsafe.ReadUnaligned<short>(ref MemoryMarshal.GetReference(span));

            if (!BitConverter.IsLittleEndian)
                result = BinaryPrimitives.ReverseEndianness(result);

            return result;
        }
        public static int ReadInt(this ReadOnlySpan<byte> span)
        {
            var result = Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span));

            if (!BitConverter.IsLittleEndian)
                result = BinaryPrimitives.ReverseEndianness(result);

            return result;
        }
        public static long ReadLong(this ReadOnlySpan<byte> span)
        {
            var result = Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(span));

            if (!BitConverter.IsLittleEndian)
                result = BinaryPrimitives.ReverseEndianness(result);

            return result;
        }
    }
}
