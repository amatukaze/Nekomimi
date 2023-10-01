using Microsoft.AspNetCore.Http;

namespace Sakuno.Nekomimi;

public sealed class NekomimiOptions
{
    public HostString ReverseProxyEndpoint { get; set; }
}
