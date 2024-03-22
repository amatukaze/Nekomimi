using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;
using System.Text;

namespace Sakuno.Nekomimi;

internal sealed class TunnelForwardMiddleware(IOptions<NekomimiOptions> options, ITunnelConnectionProvider tunnelConnectionProvider)
{
    private static readonly byte[] OkResponse = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");

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
        var upstream = await tunnelConnectionProvider.CreatePipeAsync(destination, context.ConnectionClosed);

        if (!_options.ReverseProxyEndpoint.HasValue)
            await context.Transport.Output.WriteAsync(OkResponse, context.ConnectionClosed);

        await Task.WhenAny(
            context.Transport.Input.CopyToAsync(upstream.Output, context.ConnectionClosed),
            upstream.Input.CopyToAsync(context.Transport.Output, context.ConnectionClosed)
        );
    }
}
