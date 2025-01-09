using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;

namespace Sakuno.Nekomimi.Tests;

public class TunnelForwardTests
{
    [Fact]
    public async Task NonTunnel()
    {
        var builder = WebApplication.CreateBuilder();
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
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("https://127.0.0.1:0");

        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/tunnel", () => "Tunnel Ok");

        await upstream.StartAsync(TestContext.Current.CancellationToken);

        var builder = WebApplication.CreateBuilder();
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

    [Fact]
    public async Task TunnelViaUpstreamReverseProxy()
    {
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("https://127.0.0.1:0");

        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/tunnel", () => "Tunnel Ok");

        await upstream.StartAsync(TestContext.Current.CancellationToken);

        var builder1 = WebApplication.CreateBuilder();
        builder1.WebHost.UseNekomimi().UseNekomimiTesting();

        await using var host1 = builder1.Build();

        await host1.StartAsync(TestContext.Current.CancellationToken);

        var reverseProxyPort = host1.GetPort();
        var builder2 = WebApplication.CreateBuilder();
        builder2.WebHost.UseNekomimiTesting(options =>
        {
            options.ReverseProxyEndpoint = new("localhost", reverseProxyPort);
        });

        await using var host2 = builder2.Build();

        await host2.StartAsync(TestContext.Current.CancellationToken);

        using var client = new HttpClient(new SocketsHttpHandler()
        {
            UseProxy = true,
            Proxy = new WebProxy("localhost", host2.GetPort()),
            SslOptions = new()
            {
                RemoteCertificateValidationCallback = delegate { return true; },
            },
        });

        using var response = await client.GetAsync($"https://localhost:{upstream.GetPort()}/tunnel", TestContext.Current.CancellationToken);

        Assert.Equal("Tunnel Ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
