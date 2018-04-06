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
                request.Method = HttpConstants.MapMethod(method) ?? new HttpMethod(new Utf8String(customMethod).ToString());
                request.Version = HttpConstants.MapVersion(version);
                request.RequestUri = new Uri(new Utf8String(target).ToString());
            }

            public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
            {
                request.Headers.TryAddWithoutValidation(new Utf8String(name).ToString(), new Utf8String(value).ToString());
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
                bool downStreamCompleted = false;
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

                    downStreamCompleted = true;
                }
                catch (Exception ex)
                {
                    SessionFailed?.Invoke(session, ex);
                    if (!downStreamCompleted)
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
