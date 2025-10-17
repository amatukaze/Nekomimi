using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;

namespace Sakuno.Nekomimi.Tests;

public class TunnelForwardTests
{
    [Fact]
    public async Task NonTunnel()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseNekomimiTesting();

        await using var host = builder.Build();
        host.MapGet("/a", () => "Preprocess A");
        host.MapGet("/b", () => "Preprocess B");

        await host.StartAsync(TestContext.Current.CancellationToken);

        using var client = new HttpClient(new SocketsHttpHandler()
        {
            UseProxy = true,
            Proxy = new WebProxy("localhost", host.GetPort()),
        });

        using (var response = await client.GetAsync("http://testhost/a", TestContext.Current.CancellationToken))
            Assert.Equal("Preprocess A", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using (var response = await client.GetAsync("http://testhost/b", TestContext.Current.CancellationToken))
            Assert.Equal("Preprocess B", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DirectTunnel()
    {
        var upstreamBuilder = WebApplication.CreateSlimBuilder();
        upstreamBuilder.WebHost.UseUrls("https://127.0.0.1:0");
        upstreamBuilder.WebHost.UseKestrelHttpsConfiguration();

        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/tunnel", () => "Tunnel Ok");

        await upstream.StartAsync(TestContext.Current.CancellationToken);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseNekomimi().UseNekomimiTesting();

        await using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        using var client = new HttpClient(new SocketsHttpHandler()
        {
            UseProxy = true,
            Proxy = new WebProxy("localhost", host.GetPort()),
            SslOptions = new()
            {
                RemoteCertificateValidationCallback = delegate { return true; },
            },
        });

        using var response = await client.GetAsync($"https://localhost:{upstream.GetPort()}/tunnel", TestContext.Current.CancellationToken);

        Assert.Equal("Tunnel Ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
