using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines.Networking.Sockets;
using System.IO.Pipelines.Text.Primitives;
using System.Net;
using System.Text.Formatting;
using System.Text.Http.Parser;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    public partial class ProxyServer
    {
        private struct ResponseLine : IHttpResponseLineHandler, IHttpHeadersHandler
        {
            public ushort Status;

            public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value) { }
            public void OnStatusLine(Http.Version version, ushort status, ReadOnlySpan<byte> reason) => Status = status;

            public bool ParseResponse(ref ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
            {
                var result = HttpParser.ParseResponseLine(ref this, ref buffer, out int consumedBytes);
                consumed = examined = buffer.Slice(consumedBytes - 1).Start; //Bug for ParseResponseLine @20180407
                return result;
            }
        }

        private async ValueTask<SocketConnection> BuildSSLAsync(RequestBuilder builder)
        {
            var upstream = UpstreamProxy;
            var request = builder.Session.Request;
            var destination = request.RequestUri;
            if (upstream == null)
            {
                foreach (var ip in await Dns.GetHostAddressesAsync(destination.DnsSafeHost))
                    try
                    {
                        return await SocketConnection.ConnectAsync(new IPEndPoint(ip, destination.Port));
                    }
                    catch { }
            }
            else
            {
                var upstreamUri = upstream.GetProxy(destination);
                foreach (var upstreamIp in await Dns.GetHostAddressesAsync(upstreamUri.DnsSafeHost))
                {
                    SocketConnection connection = null;
                    try
                    {
                        connection = await SocketConnection.ConnectAsync(new IPEndPoint(upstreamIp, upstreamUri.Port));
                        var outputText = connection.Output.AsTextOutput(SymbolTable.InvariantUtf8);
                        var parser = new HttpParser();

                        outputText.Format("CONNECT {0}:{1} HTTP/1.1\r\n",
                                destination.Host,
                                destination.Port);
                        foreach (var header in request.Headers)
                            outputText.Format("{0}: {1}\r\n",
                                header.Key,
                                string.Join("; ", header.Value));
                        foreach (var header in builder.PendingHeaders)
                            outputText.Format("{0}: {1}\r\n",
                                header.Key,
                                header.Value);
                        outputText.Append("\r\n");
                        await outputText.FlushAsync();

                        ResponseLine response = default;
                        var result = await connection.Input.ReadAsync();
                        var buffer = result.Buffer;
                        SequencePosition consumed = buffer.Start, examined = buffer.Start;

                        do buffer = await MoreAsync(connection.Input, consumed, examined);
                        while (!response.ParseResponse(ref buffer, out consumed, out examined));

                        do buffer = await MoreAsync(connection.Input, consumed, examined);
                        while (!parser.ParseHeaders(response, buffer, out consumed, out examined, out _));

                        connection.Input.AdvanceTo(consumed, examined);

                        if (response.Status >= 200 && response.Status < 300)
                            return connection;

                        await connection.DisposeAsync();
                    }
                    catch
                    {
                        if (connection != null)
                            await connection.DisposeAsync();
                    }
                }
            }

            return null;
        }
    }
}
