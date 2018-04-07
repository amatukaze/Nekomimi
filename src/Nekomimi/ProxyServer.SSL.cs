using System;
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
        private struct ResponseLine : IHttpResponseLineHandler
        {
            public ushort Status;
            public void OnStatusLine(Http.Version version, ushort status, ReadOnlySpan<byte> reason) => Status = status;
        }
        private async ValueTask<SocketConnection> BuildSSLAsync(RequestBuilder builder)
        {
            var request = builder.Session.Request;
            var uri = request.RequestUri;
            foreach (var ip in await Dns.GetHostAddressesAsync(uri.DnsSafeHost))
                try
                {
                    var connection = await SocketConnection.ConnectAsync(new IPEndPoint(ip, uri.Port));
                    var outputText = connection.Output.AsTextOutput(SymbolTable.InvariantUtf8);

                    try
                    {
                        /*outputText.Format("CONNECT {0}:{1} HTTP/1.1\r\n",
                            uri.Host,
                            uri.Port);
                        foreach (var header in request.Headers)
                            outputText.Format("{0}: {1}\r\n",
                                header.Key,
                                string.Join("; ", header.Value));
                        foreach (var header in builder.PendingContentHeaders)
                            outputText.Format("{0}: {1}\r\n",
                                header.Key,
                                header.Value);
                        outputText.Append("\r\n");

                        await outputText.FlushAsync();
                        ResponseLine response = default;
                        while (true)
                        {
                            var result = await connection.Input.ReadAsync();
                            var buffer = result.Buffer;
                            if (result.IsCanceled && buffer.IsEmpty)
                            {
                                await connection.DisposeAsync();
                                break;
                            }
                            int consumed = 0;
                            try
                            {
                                if (HttpParser.ParseResponseLine(ref response, ref buffer, out consumed)) break;
                            }
                            finally
                            {
                                connection.Input.AdvanceTo(buffer.Slice(consumed).Start);
                            }
                        }
                        if (response.Status != 200)
                        {
                            await connection.DisposeAsync();
                            continue;
                        }*/
                        return connection;
                    }
                    catch
                    {
                        await connection.DisposeAsync();
                    }
                }
                catch { }

            return null;
        }
    }
}
