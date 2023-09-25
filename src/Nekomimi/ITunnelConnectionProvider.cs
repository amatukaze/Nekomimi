using Microsoft.AspNetCore.Http;
using System.IO.Pipelines;

namespace Sakuno.Nekomimi;

public interface ITunnelConnectionProvider
{
    ValueTask<IDuplexPipe> CreatePipeAsync(HostString destionation, CancellationToken cancellationToken);
}
