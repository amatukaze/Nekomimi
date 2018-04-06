using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;
using System.Linq;
using System.Net.Http;
using System.Text.Formatting;
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

        private static async ValueTask<ReadOnlySequence<byte>> MoreAsync(PipeReader reader, SequencePosition consumed, SequencePosition examined)
        {
            reader.AdvanceTo(consumed, examined);
            var result = await reader.ReadAsync();
            if (result.IsCompleted && result.Buffer.IsEmpty)
                throw new FormatException("Request incomplete.");
            return result.Buffer;
        }

        private async Task HandleConnection(IDuplexPipe connection)
        {
            var parser = new HttpParser(true);
            var outputText = connection.Output.AsTextOutput(SymbolTable.InvariantUtf8);

            for (var result = await connection.Input.ReadAsync();
                !(result.IsCanceled && result.Buffer.IsEmpty);
                result = await connection.Input.ReadAsync())
            {
                var session = new Session();
                try
                {
                    var builder = new RequestBuilder(session.Request);
                    ReadOnlySequence<byte> buffer = result.Buffer;
                    SequencePosition consumed = buffer.Start, examined = buffer.Start;

                    do buffer = await MoreAsync(connection.Input, consumed, examined);
                    while (!parser.ParseRequestLine(builder, in buffer, out consumed, out examined));

                    do buffer = await MoreAsync(connection.Input, consumed, examined);
                    while (!parser.ParseHeaders(builder, in buffer, out consumed, out examined, out _));

                    connection.Input.AdvanceTo(consumed, examined);

                    if (session.Request.Headers.ExpectContinue == true)
                    {
                        outputText.Append("100 Continue");
                        await connection.Output.FlushAsync();
                    }

                    if (session.Request.Headers.TryGetValues("Content-Length", out var lengthStr)
                        && int.TryParse(lengthStr.Single(), out var length))
                    {
                        var requestBody = new byte[length];
                        ArraySegment<byte> remained;
                        while (remained.Count > 0)
                        {
                            int bytesRead = await connection.Input.ReadAsync(remained);
                            if (bytesRead == 0)
                                throw new FormatException("Incomplete request content");
                            remained = new ArraySegment<byte>(requestBody, remained.Offset + bytesRead, remained.Count - bytesRead);
                        }
                    }
                }
                catch (Exception ex)
                {
                    SessionFailed?.Invoke(session, ex);
                    break;
                }
                finally
                {
                    parser.Reset();
                }
            }
        }
    }
}
