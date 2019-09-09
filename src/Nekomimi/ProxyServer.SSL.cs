using System.Buffers;
using System.IO.Pipelines;
using System.IO.Pipelines.Networking.Sockets;
using System.Net;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    public partial class ProxyServer
    {
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
                        var output = connection.Output;

                        WriteUtf8(output,
                            $"CONNECT {destination.Host}:{destination.Port} HTTP/1.1\r\n");
                        foreach (var header in request.Headers)
                            WriteUtf8(output,
                                $"{header.Key}: {string.Join("; ", header.Value)}\r\n");
                        foreach (var header in builder.PendingHeaders)
                            WriteUtf8(output,
                                $"{header.Key}: {string.Join("; ", header.Value)}\r\n");
                        WriteUtf8(output, "\r\n");
                        await output.FlushAsync();

                        if (await TryGet200Async(connection.Input))
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

        private static async ValueTask<bool> TryGet200Async(PipeReader reader)
        {
            while (true)
            {
                var result = await reader.ReadAsync().ConfigureAwait(false);
                var buffer = result.Buffer;

                if (buffer.Length > 12)
                {
                    var array = buffer.ToArray();
                    if (array[0] != (byte)'H' ||
                        array[1] != (byte)'T' ||
                        array[2] != (byte)'T' ||
                        array[3] != (byte)'P' ||
                        array[4] != (byte)'/' ||
                        array[6] != (byte)'.' ||
                        array[8] != (byte)' ' ||
                        array[9] != (byte)'2' ||
                        array[10] != (byte)'0' ||
                        array[11] != (byte)'0')
                        goto fail;
                    for (int i = 13; i < array.Length; i++)
                        if (array[i - 1] == (byte)'\r' &&
                            array[i] == (byte)'\n')
                        {
                            for (int j = i + 2; j < array.Length; j++)
                                if (array[j - 1] == (byte)'\r' &&
                                    array[j] == (byte)'\n')
                                {
                                    reader.AdvanceTo(buffer.Slice(j + 1).Start);
                                    return true;
                                }
                            goto fail;
                        }
                }
fail:
                if (result.IsCompleted)
                    return false;
            }
        }
    }
}
