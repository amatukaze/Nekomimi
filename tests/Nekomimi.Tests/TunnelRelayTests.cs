using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

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

    [Fact]
    public async Task TlsTunnelUsesUpstreamCertificateValidationCallback()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var cancellationToken = timeout.Token;

        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var upstreamPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var upstreamTask = RunUpstreamAsync();
            var validationCallbacks = 0;

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseNekomimi(options =>
            {
                options.UpstreamCertificateValidationCallback = (_, remoteCertificate, _, _) =>
                {
                    Interlocked.Increment(ref validationCallbacks);
                    return remoteCertificate!.Equals(certificate);
                };
            }).ConfigureKestrel(kestrel =>
            {
                kestrel.Listen(IPAddress.Loopback, 0, listenOptions =>
                {
                    listenOptions.UseHttps(certificate);
                    listenOptions.UseNekomimi();
                });
            });

            await using var host = builder.Build();
            await host.StartAsync(cancellationToken);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, host.GetPort(), cancellationToken);
            await using var stream = new SslStream(client.GetStream());
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = delegate { return true; },
            }, cancellationToken);

            var authority = $"127.0.0.1:{upstreamPort}";
            var connectRequest = Encoding.ASCII.GetBytes($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n\r\n");
            await stream.WriteAsync(connectRequest, cancellationToken);

            var expectedConnectResponse = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            var actualConnectResponse = new byte[expectedConnectResponse.Length];
            await stream.ReadExactlyAsync(actualConnectResponse, cancellationToken);
            Assert.Equal(expectedConnectResponse, actualConnectResponse);

            await stream.WriteAsync("ping"u8.ToArray(), cancellationToken);
            var response = new byte[4];
            await stream.ReadExactlyAsync(response, cancellationToken);
            Assert.Equal("pong"u8.ToArray(), response);

            await upstreamTask;
            Assert.Equal(1, validationCallbacks);
        }
        finally
        {
            listener.Stop();
        }

        async Task RunUpstreamAsync()
        {
            using var upstream = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = new SslStream(upstream.GetStream());
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
            }, cancellationToken);

            var request = new byte[4];
            await stream.ReadExactlyAsync(request, cancellationToken);
            Assert.Equal("ping"u8.ToArray(), request);

            await stream.WriteAsync("pong"u8.ToArray(), cancellationToken);
        }
    }
}
