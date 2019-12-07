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
            var result = Unsafe.As<byte, short>(ref Unsafe.AsRef(in MemoryMarshal.GetReference(span)));

            if (!BitConverter.IsLittleEndian)
                result = BinaryPrimitives.ReverseEndianness(result);

            return result;
        }
        public static int ReadInt(this ReadOnlySpan<byte> span)
        {
            var result = Unsafe.As<byte, int>(ref Unsafe.AsRef(in MemoryMarshal.GetReference(span)));

            if (!BitConverter.IsLittleEndian)
                result = BinaryPrimitives.ReverseEndianness(result);

            return result;
        }
        public static long ReadLong(this ReadOnlySpan<byte> span)
        {
            var result = Unsafe.As<byte, long>(ref Unsafe.AsRef(in MemoryMarshal.GetReference(span)));

            if (!BitConverter.IsLittleEndian)
                result = BinaryPrimitives.ReverseEndianness(result);

            return result;
        }
    }
}
