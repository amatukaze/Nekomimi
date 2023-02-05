using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using System.Buffers;
using System.Text;
using KestrelHttpMethod = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpMethod;

namespace Sakuno.Nekomimi;

internal sealed class PreprocessMiddleware
{
    private readonly HttpParser<HttpRequest> _httpParser = new();

    public async Task HandleAsync(ConnectionContext context, ConnectionDelegate next)
    {
        var request = await ParseRequest(context);

        if (request.IsTunnel)
            throw new NotImplementedException();

        await next(context);
    }

    private async Task<HttpRequest> ParseRequest(ConnectionContext context)
    {
        var request = new HttpRequest();
        var input = context.Transport.Input;

        while (true)
        {
            var result = await input.ReadAsync(context.ConnectionClosed);

            if (result.IsCanceled)
                break;

            var buffer = result.Buffer;

            if (!ParseBuffer(request, buffer, out var consumed))
                input.AdvanceTo(buffer.Start, buffer.End);
            else
            {
                input.AdvanceTo(buffer.Start);
                break;
            }

            if (result.IsCompleted)
                break;
        }

        return request;

        bool ParseBuffer(HttpRequest request, ReadOnlySequence<byte> buffer, out SequencePosition consumed)
        {
            var reader = new SequenceReader<byte>(buffer);

            if (_httpParser.ParseRequestLine(request, ref reader) && _httpParser.ParseHeaders(request, ref reader))
            {
                consumed = reader.Position;
                return true;
            }

            consumed = default;
            return false;
        }
    }

    private sealed class HttpRequest : IHttpRequestLineHandler, IHttpHeadersHandler
    {
        public bool IsTunnel { get; private set; }
        public HostString Destination { get; private set; }

        void IHttpRequestLineHandler.OnStartLine(HttpVersionAndMethod versionAndMethod, TargetOffsetPathLength targetPath, Span<byte> startLine)
        {
            if (versionAndMethod.Method is not KestrelHttpMethod.Connect)
                return;

            IsTunnel = true;
            Destination = new(Encoding.ASCII.GetString(startLine.Slice(targetPath.Offset, targetPath.Length)));
        }

        void IHttpHeadersHandler.OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
        {
        }
        void IHttpHeadersHandler.OnHeadersComplete(bool endStream)
        {
        }

        void IHttpHeadersHandler.OnStaticIndexedHeader(int index)
        {
        }
        void IHttpHeadersHandler.OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value)
        {
        }
    }
}
