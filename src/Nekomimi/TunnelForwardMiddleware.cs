using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Sakuno.Nekomimi;

internal sealed class TunnelForwardMiddleware(IOptions<NekomimiOptions> options)
{
    private readonly NekomimiOptions _options = options.Value;

    public Task HandleAsync(ConnectionContext context, ConnectionDelegate next)
    {
        var request = context.Features.Get<INekomimiContextFeature>();

        if (request is not { IsTunnel: true })
            return next(context);

        return ForwardCoreAsync(context, next, request);
    }
    private async Task ForwardCoreAsync(ConnectionContext context, ConnectionDelegate next, INekomimiContextFeature request)
    {
        if (await DetermineHttpRequestLineAsync(context))
        {
            await next(context);
            return;
        }

        var destination = !_options.ReverseProxyEndpoint.HasValue ? request.Destination : _options.ReverseProxyEndpoint;

        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

        await socket.ConnectAsync(destination.Host, destination.Port.GetValueOrDefault(), context.ConnectionClosed);

        Stream stream = new NetworkStream(socket, ownsSocket: false);

        if (context.Features.Get<ITlsConnectionFeature>() is not null)
        {
            var sslStream = new SslStream(stream, leaveInnerStreamOpen: true);

            await sslStream.AuthenticateAsClientAsync(destination.Host);

            stream = sslStream;
        }

        await Task.WhenAny(
            stream.CopyToAsync(context.Transport.Output, context.ConnectionClosed),
            context.Transport.Input.CopyToAsync(stream, context.ConnectionClosed));
    }

    private async ValueTask<bool> DetermineHttpRequestLineAsync(ConnectionContext context)
    {
        var input = context.Transport.Input;
        var readResult = await input.ReadAsync(context.ConnectionClosed);

        var result = CheckCore(readResult.Buffer.FirstSpan);

        input.AdvanceTo(readResult.Buffer.Start);

        return result;

        static bool CheckCore(ReadOnlySpan<byte> input)
        {
            var index = input.IndexOf("\r\n"u8);
            if (index is -1)
                return false;

            var requestLine = input[..index];

            return requestLine.EndsWith(" HTTP/2.0"u8) ||
                   requestLine.EndsWith(" HTTP/1.1"u8);
        }
    }
}
