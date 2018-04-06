using System;
using System.Net.Http;
using System.Text.Http.Parser;

namespace Sakuno.Nekomimi
{
    internal static class HttpConstants
    {
        public static HttpMethod MapMethod(Http.Method method)
        {
            switch (method)
            {
                case Http.Method.Get:
                    return HttpMethod.Get;
                case Http.Method.Put:
                    return HttpMethod.Put;
                case Http.Method.Delete:
                    return HttpMethod.Delete;
                case Http.Method.Post:
                    return HttpMethod.Post;
                case Http.Method.Head:
                    return HttpMethod.Head;
                case Http.Method.Trace:
                    return HttpMethod.Trace;
                case Http.Method.Patch:
                    return new HttpMethod("PATCH");
                case Http.Method.Connect:
                    return new HttpMethod("CONNECT");
                case Http.Method.Options:
                    return new HttpMethod("OPTIONS");
                default:
                    return null;
            }
        }

        private static Version Version10 { get; } = new Version(1, 0);
        private static Version Version11 { get; } = new Version(1, 1);
        private static Version Version20 { get; } = new Version(2, 0);
        public static Version MapVersion(Http.Version version)
        {
            switch (version)
            {
                case Http.Version.Http10:
                    return Version10;
                case Http.Version.Http11:
                    return Version11;
                case Http.Version.Http20:
                    return Version20;
                default:
                    throw new FormatException("Unknown http version");
            }
        }
    }
}
