using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Sakuno.Nekomimi;

internal sealed class TunnelConnectionProvider : ITunnelConnectionProvider, IDisposable
{
    private readonly SocketConnectionContextFactory _factory;

    public TunnelConnectionProvider(IOptions<SocketConnectionFactoryOptions> options, ILogger<TunnelConnectionProvider> logger)
    {
        _factory = new SocketConnectionContextFactory(options.Value, logger);
    }

    public async ValueTask<IDuplexPipe> CreatePipeAsync(HostString destionation, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

        await socket.ConnectAsync(destionation.Host, destionation.Port.GetValueOrDefault(), cancellationToken);

        return _factory.Create(socket).Transport;
    }

    public void Dispose() => _factory.Dispose();
}
