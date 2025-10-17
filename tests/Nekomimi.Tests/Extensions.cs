using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sakuno.Nekomimi.Tests;

internal static class Extensions
{
    public static IWebHostBuilder UseNekomimiTesting(this IWebHostBuilder builder) =>
        builder.UseNekomimiTesting(delegate { });
    public static IWebHostBuilder UseNekomimiTesting(this IWebHostBuilder builder, Action<NekomimiOptions> options)
    {
        builder.UseNekomimi(options).ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions =>
            {
                listenOptions.UseNekomimiPreprocessMiddleware();
                listenOptions.UseNekomimiTunnelForwardMiddleware();
            });
        });

        return builder;
    }

    public static int GetPort(this IHost host)
    {
        var server = host.Services.GetRequiredService<IServer>();
        var uri = new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.First());

        return uri.Port;
    }
}
