using System;
using System.Collections.Concurrent;
using System.Text;

namespace Sakuno.Nekomimi
{
    static class HttpConstants
    {
        public static readonly byte[] Whitespace = new byte[] { (byte)' ' };
        public static readonly byte[] CrLf = new byte[] { (byte)'\r', (byte)'\n' };

        public static class Versions
        {
            public static readonly byte[] Version10 =
                new byte[] { (byte)'H', (byte)'T', (byte)'T', (byte)'P', (byte)'/', (byte)'1', (byte)'.', (byte)'0' };
            public static readonly byte[] Version11 =
                new byte[] { (byte)'H', (byte)'T', (byte)'T', (byte)'P', (byte)'/', (byte)'1', (byte)'.', (byte)'1' };
        }

        public static class Methods
        {
            public static readonly byte[] Get = new byte[] { (byte)'G', (byte)'E', (byte)'T' };
            public static readonly byte[] Head = new byte[] { (byte)'H', (byte)'E', (byte)'A', (byte)'D' };
            public static readonly byte[] Post = new byte[] { (byte)'P', (byte)'O', (byte)'S', (byte)'T' };
            public static readonly byte[] Put = new byte[] { (byte)'P', (byte)'U', (byte)'T' };
            public static readonly byte[] Delete = new byte[] { (byte)'D', (byte)'E', (byte)'L', (byte)'E', (byte)'T', (byte)'E' };
            public static readonly byte[] Connect = new byte[] { (byte)'C', (byte)'O', (byte)'N', (byte)'N', (byte)'E', (byte)'C', (byte)'T' };
            public static readonly byte[] Options = new byte[] { (byte)'O', (byte)'P', (byte)'T', (byte)'I', (byte)'O', (byte)'N', (byte)'S' };
            public static readonly byte[] Trace = new byte[] { (byte)'T', (byte)'R', (byte)'A', (byte)'C', (byte)'E' };
            public static readonly byte[] Patch = new byte[] { (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H' };
        }

        public static class Headers
        {
            public static readonly byte[] Separator = new byte[] { (byte)':', (byte)' ' };

            public static readonly ConcurrentDictionary<string, byte[]> NameCache =
                new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        }

        public static byte[] FromMethod(this HttpMethod method)
        {
            switch (method)
            {
                case HttpMethod.Get:
                    return Methods.Get;

                case HttpMethod.Head:
                    return Methods.Head;

                case HttpMethod.Post:
                    return Methods.Post;

                case HttpMethod.Put:
                    return Methods.Put;

                case HttpMethod.Delete:
                    return Methods.Delete;

                case HttpMethod.Connect:
                    return Methods.Connect;

                case HttpMethod.Options:
                    return Methods.Options;

                case HttpMethod.Trace:
                    return Methods.Trace;

                case HttpMethod.Patch:
                    return Methods.Patch;

                default: throw new ArgumentOutOfRangeException(nameof(method));
            }
        }

        public static byte[] FromVersion(HttpVersion version)
        {
            switch (version)
            {
                case HttpVersion.Version10:
                    return Versions.Version10;

                case HttpVersion.Version11:
                    return Versions.Version11;

                default: throw new ArgumentOutOfRangeException(nameof(version));
            }
        }

        public static byte[] FromHeaderName(string name) =>
            Headers.NameCache.GetOrAdd(name, Encoding.ASCII.GetBytes);
    }
}
