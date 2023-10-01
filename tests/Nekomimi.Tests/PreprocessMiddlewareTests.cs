using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;

namespace Sakuno.Nekomimi.Tests;

public class PreprocessMiddlewareTests
{
    [Fact]
    public async Task NonTunnel()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseNekomimi().UseUrls("http://127.0.0.1:0");

        await using var host = builder.Build();
        host.MapGet("/a", () => "Preprocess A");
        host.MapGet("/b", () => "Preprocess B");

        await host.StartAsync();

        using var client = new HttpClient(new SocketsHttpHandler()
        {
            UseProxy = true,
            Proxy = new WebProxy("localhost", host.GetPort()),
        });

        using (var response = await client.GetAsync("http://testhost/a"))
            Assert.Equal("Preprocess A", await response.Content.ReadAsStringAsync());

        using (var response = await client.GetAsync("http://testhost/b"))
            Assert.Equal("Preprocess B", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DirectTunnel()
    {
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("https://127.0.0.1:0");

        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/tunnel", () => "Tunnel Ok");

        await upstream.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseNekomimi().UseUrls("http://127.0.0.1:0");

        await using var host = builder.Build();

        await host.StartAsync();

        using var client = new HttpClient(new SocketsHttpHandler()
        {
            UseProxy = true,
            Proxy = new WebProxy("localhost", host.GetPort()),
            SslOptions = new()
            {
                RemoteCertificateValidationCallback = delegate { return true; },
            },
        });

        using var response = await client.GetAsync($"https://localhost:{upstream.GetPort()}/tunnel");

        Assert.Equal("Tunnel Ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TunnelViaUpstreamReverseProxy()
    {
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("https://127.0.0.1:0");

        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/tunnel", () => "Tunnel Ok");

        await upstream.StartAsync();

        var builder1 = WebApplication.CreateBuilder();
        builder1.WebHost.UseNekomimi().UseUrls("http://127.0.0.1:0");

        await using var host1 = builder1.Build();

        await host1.StartAsync();

        var reverseProxyPort = host1.GetPort();
        var builder2 = WebApplication.CreateBuilder();
        builder2.WebHost.UseNekomimi(options =>
        {
            options.ReverseProxyEndpoint = new("localhost", reverseProxyPort);
        }).UseUrls("http://127.0.0.1:0");

        await using var host2 = builder2.Build();

        await host2.StartAsync();

        using var client = new HttpClient(new SocketsHttpHandler()
        {
            UseProxy = true,
            Proxy = new WebProxy("localhost", host2.GetPort()),
            SslOptions = new()
            {
                RemoteCertificateValidationCallback = delegate { return true; },
            },
        });

        using var response = await client.GetAsync($"https://localhost:{upstream.GetPort()}/tunnel");

        Assert.Equal("Tunnel Ok", await response.Content.ReadAsStringAsync());
    }
}
