using System;
using System.Net.Http;
using KestrelMethod = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpMethod;
using KestrelVersion = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpVersion;

namespace Sakuno.Nekomimi
{
    internal static class HttpConstants
    {
        public static HttpMethod MapMethod(KestrelMethod method)
            => method switch
            {
                KestrelMethod.Get => HttpMethod.Get,
                KestrelMethod.Put => HttpMethod.Put,
                KestrelMethod.Delete => HttpMethod.Delete,
                KestrelMethod.Post => HttpMethod.Post,
                KestrelMethod.Head => HttpMethod.Head,
                KestrelMethod.Trace => HttpMethod.Trace,
                KestrelMethod.Patch => new HttpMethod("PATCH"),
                KestrelMethod.Connect => new HttpMethod("CONNECT"),
                KestrelMethod.Options => new HttpMethod("OPTIONS"),
                _ => null,
            };

        private static Version Version10 { get; } = new Version(1, 0);
        private static Version Version11 { get; } = new Version(1, 1);
        private static Version Version20 { get; } = new Version(2, 0);
        public static Version MapVersion(KestrelVersion version)
            => version switch
            {
                KestrelVersion.Http10 => Version10,
                KestrelVersion.Http11 => Version11,
                KestrelVersion.Http2 => Version20,
                _ => throw new FormatException("Unknown http version"),
            };
    }
}
