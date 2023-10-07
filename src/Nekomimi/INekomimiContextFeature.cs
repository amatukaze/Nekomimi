using Microsoft.AspNetCore.Http;

namespace Sakuno.Nekomimi;

internal interface INekomimiContextFeature
{
    bool IsTunnel { get; }
    HostString Destination { get; }
}
