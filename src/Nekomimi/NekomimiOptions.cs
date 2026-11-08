using System.Net.Security;
using Microsoft.AspNetCore.Http;

namespace Sakuno.Nekomimi;

public sealed class NekomimiOptions
{
    public HostString ReverseProxyEndpoint { get; set; }
    public RemoteCertificateValidationCallback? UpstreamCertificateValidationCallback { get; set; }
}
