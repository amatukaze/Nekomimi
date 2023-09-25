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
}
