using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sakuno.Nekomimi.Tests;

internal static class Extensions
{
    public static int GetPort(this IHost host)
    {
        var server = host.Services.GetRequiredService<IServer>();
        var uri = new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.First());

        return uri.Port;
    }
}
