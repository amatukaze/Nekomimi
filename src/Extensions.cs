using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FxHttpMethod = System.Net.Http.HttpMethod;
using FxHttpVersion = System.Net.HttpVersion;

namespace Sakuno.Nekomimi
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

        public static unsafe string GetAsciiString(this ReadOnlySpan<byte> span)
        {
#if NETSTANDARD2_1
            return Encoding.ASCII.GetString(span);
#else
            fixed (byte* ptr = span)
                return Encoding.ASCII.GetString(ptr, span.Length);
#endif
        }

        internal static FxHttpMethod AsFxHttpMethod(this HttpMethod method) => method switch
        {
            HttpMethod.Get => FxHttpMethod.Get,
            HttpMethod.Post => FxHttpMethod.Post,
            HttpMethod.Put => FxHttpMethod.Put,
            HttpMethod.Head => FxHttpMethod.Get,
            HttpMethod.Trace => FxHttpMethod.Trace,
            HttpMethod.Options => FxHttpMethod.Options,
            HttpMethod.Delete => FxHttpMethod.Delete,

#if NETSTANDARD2_1
            HttpMethod.Patch => FxHttpMethod.Patch,
#else
            HttpMethod.Patch => new FxHttpMethod("PATCH"),
#endif
            HttpMethod.Connect => new FxHttpMethod("CONNECT"),

            _ => throw new ArgumentException(nameof(method)),
        };
        internal static Version AsFxVersion(this HttpVersion version) => version switch
        {
            HttpVersion.Http10 => FxHttpVersion.Version10,
            HttpVersion.Http11 => FxHttpVersion.Version11,

#if NETSTANDARD2_1
            HttpVersion.Http2 => FxHttpVersion.Version20,
#endif

            _ => throw new ArgumentException(nameof(version)),
        };

#if !NETSTANDARD2_1
        internal static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
        {
            key = kvp.Key;
            value = kvp.Value;
        }
#endif

        public static HttpVersion AsHttpVersion(this Version version) => (version.Major, version.Minor) switch
        {
            (1, 0) => HttpVersion.Http10,
            (1, 2) => HttpVersion.Http11,
            (2, 0) => HttpVersion.Http2,

            _ => throw new InvalidOperationException("Unknown version"),
        };
    }
}
