using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;

namespace Sakuno.Nekomimi;

internal sealed class TunnelForwardMiddleware(IOptions<NekomimiOptions> options)
{
    private readonly NekomimiOptions _options = options.Value;

    public Task HandleAsync(ConnectionContext context, ConnectionDelegate next)
    {
        var request = context.Features.Get<INekomimiContextFeature>();

        if (request is not { IsTunnel: true })
            return next(context);

        return ForwardCoreAsync(context, request);
    }
    private async Task ForwardCoreAsync(ConnectionContext context, INekomimiContextFeature request)
    {
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
