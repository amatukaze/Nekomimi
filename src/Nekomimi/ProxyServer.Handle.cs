using System;
using System.IO.Pipelines;
using System.Net.Http;
using System.Text.Http.Parser;
using System.Text.Utf8;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    public partial class ProxyServer
    {
        private struct RequestBuilder : IHttpRequestLineHandler, IHttpHeadersHandler
        {
            private HttpRequestMessage request;
            public RequestBuilder(HttpRequestMessage request) => this.request = request;

            public void OnStartLine(Http.Method method, Http.Version version, ReadOnlySpan<byte> target, ReadOnlySpan<byte> path, ReadOnlySpan<byte> query, ReadOnlySpan<byte> customMethod, bool pathEncoded)
            {
                request.Method = MapMethod(method) ?? new HttpMethod(new Utf8String(customMethod).ToString());
                request.Version = MapVersion(version);
                request.RequestUri = new Uri(new Utf8String(target).ToString());
            }

            public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
            {
                request.Headers.TryAddWithoutValidation(new Utf8String(name).ToString(), new Utf8String(value).ToString());
            }

            private static HttpMethod MapMethod(Http.Method method)
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
            private static Version MapVersion(Http.Version version)
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
        private enum HttpBuilderState
        {
            Init,
            AfterRequest,
            AfterHeader
        }
        private async Task HandleConnection(IDuplexPipe connection)
        {
            Session session = null;
            var parser = new HttpParser(true);
            HttpBuilderState state = HttpBuilderState.Init;

            while (true)
            {
                var result = await connection.Input.ReadAsync();
                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.Start;

                try
                {
                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        if (state == HttpBuilderState.Init) return;
                        else throw new FormatException("Incompleted request");
                    }
                    try
                    {
                        if (state == HttpBuilderState.Init)
                        {
                            if (session == null)
                                session = new Session();

                            if (parser.ParseRequestLine(new RequestBuilder(session.Request), in buffer, out consumed, out examined))
                                state++;
                            else continue;
                        }

                        if (state == HttpBuilderState.AfterRequest)
                        {
                            if (parser.ParseHeaders(new RequestBuilder(session.Request), in buffer, out consumed, out examined, out int _))
                                state++;
                            else continue;
                        }
                    }
                    catch (Exception e)
                    {
                        SessionFailed?.Invoke(session, e);
                        break;
                    }
                }
                finally
                {
                    connection.Input.AdvanceTo(consumed, examined);
                }
            }
        }
    }
}
