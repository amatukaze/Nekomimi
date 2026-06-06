using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Sakuno.Nekomimi;

internal sealed class TunnelRelayMiddleware(IOptions<NekomimiOptions> options)
{
    private readonly NekomimiOptions _options = options.Value;

    public async Task HandleAsync(ConnectionContext context, ConnectionDelegate next)
    {
        var request = context.Features.Get<INekomimiContextFeature>();

        if (request is not { IsTunnel: true })
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
}
