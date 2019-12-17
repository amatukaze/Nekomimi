using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    public class ProxyServer
    {
        private Socket? _listenerSocket;

        private readonly HttpClient _httpClient;

        public ProxyServer()
        {
            _httpClient = new HttpClient();
        }

        public void Start(int port)
        {
            if (_listenerSocket != null)
                throw new InvalidOperationException("Server is already running.");

            if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException(nameof(port));

            _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listenerSocket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            _listenerSocket.Listen(20);

            _ = ListenForConnectionsAsync();
        }

        private async Task ListenForConnectionsAsync()
        {
            Debug.Assert(_listenerSocket != null);

            while (true)
            {
                var clientSocket = await _listenerSocket.AcceptAsync().ConfigureAwait(false);

                SetClientSocketOptions(clientSocket);

                ThreadPool.QueueUserWorkItem(HandleClientSocketAsync, clientSocket);
            }

            static void SetClientSocketOptions(Socket socket)
            {
                if (socket.AddressFamily == AddressFamily.Unix)
                    return;

                try
                {
                    socket.NoDelay = true;
                }
                catch { }
            }
        }

        private async void HandleClientSocketAsync(object state)
        {
            var clientSocket = (Socket)state;
            var session = new HttpSession();

            await HandleRequestMessage(session.Request, clientSocket).ConfigureAwait(false);

            try
            {
                await SendRequestToRemoteHostAsync(session).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return;
            }

            Debug.Assert(session.Response != null);
        }
        private async ValueTask HandleRequestMessage(HttpRequestMessage request, Socket clientSocket)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(4096);

            try
            {
                var offset = 0;
                var length = 0;

                while (true)
                {
#if NETSTANDARD2_1
                    length = await clientSocket.ReceiveAsync(buffer.AsMemory(offset), SocketFlags.None).ConfigureAwait(false);
#else
                    length = await clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, buffer.Length - offset), SocketFlags.None).ConfigureAwait(false);
#endif
                    if (request.ParseStartLineAndHeaders(buffer.AsSpan(0, length + offset), out var consumed))
                    {
                        offset = consumed;
                        break;
                    }

                    if (consumed > 0)
                    {
                        offset = length - consumed;
                        buffer.AsSpan(consumed, length - consumed).CopyTo(buffer);
                        continue;
                    }

                    offset = buffer.Length;

                    var largerBuffer = ArrayPool<byte>.Shared.Rent(offset * 2);

                    Buffer.BlockCopy(buffer, 0, largerBuffer, 0, offset);
                    ArrayPool<byte>.Shared.Return(buffer, true);
                    buffer = largerBuffer;
                }

                if (request.IsHttps)
                {
                    // Currently we don't provide SSL decryption

                    using var upstreamSocket = await BuildSSLConnectionAsync(request).ConfigureAwait(false);

                    if (upstreamSocket == null)
                        throw new InvalidOperationException();

                    ReadOnlyMemory<byte> response = new[]
                    {
                        (byte)'H', (byte)'T', (byte)'T', (byte)'P', (byte)'/', (byte)'1', (byte)'.', (byte)'1', (byte)' ',
                        (byte)'2', (byte)'0', (byte)'0', (byte)' ',
                        (byte)'C', (byte)'o', (byte)'n', (byte)'n', (byte)'e', (byte)'c', (byte)'t', (byte)'i', (byte)'o', (byte)'n', (byte)' ',
                        (byte)'E', (byte)'s', (byte)'t', (byte)'a', (byte)'b', (byte)'l', (byte)'i', (byte)'s', (byte)'h', (byte)'e', (byte)'d',
                        (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n',
                    };

#if NETSTANDARD2_1
                    await clientSocket.SendAsync(response, SocketFlags.None).ConfigureAwait(false);
#else
                    MemoryMarshal.TryGetArray(response, out var segment);

                    await clientSocket.SendAsync(segment, SocketFlags.None).ConfigureAwait(false);
#endif

                    await Task.WhenAll(
                        BuildTunnelAsync(clientSocket, upstreamSocket),
                        BuildTunnelAsync(upstreamSocket, clientSocket)
                    ).ConfigureAwait(false);

                    return;
                }

                if (request.State == HttpRequestMessageState.HandlingExpect100Continue)
                {
                    ReadOnlyMemory<byte> response = new[]
                    {
                        (byte)'H', (byte)'T', (byte)'T', (byte)'P', (byte)'/', (byte)'1', (byte)'.', (byte)'1', (byte)' ',
                        (byte)'1', (byte)'0', (byte)'0', (byte)' ',
                        (byte)'C', (byte)'o', (byte)'n', (byte)'t', (byte)'i', (byte)'n', (byte)'u', (byte)'e',
                        (byte)'\r', (byte)'\n'
                    };

#if NETSTANDARD2_1
                    await clientSocket.SendAsync(response, SocketFlags.None).ConfigureAwait(false);
#else
                    MemoryMarshal.TryGetArray(response, out var segment);

                    await clientSocket.SendAsync(segment, SocketFlags.None).ConfigureAwait(false);
#endif

                    offset = 0;

#if NETSTANDARD2_1
                    length = await clientSocket.ReceiveAsync(buffer.AsMemory(offset), SocketFlags.None).ConfigureAwait(false);
#else
                    length = await clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, buffer.Length - offset), SocketFlags.None).ConfigureAwait(false);
#endif

                    request.State++;
                }

                while (true)
                {
                    var remaining = request.ReadBody(buffer.AsSpan(offset, length - offset));
                    if (remaining == 0)
                    {
                        request.State++;
                        break;
                    }

                    if (offset > 0)
                        offset = 0;

#if NETSTANDARD2_1
                    length = await clientSocket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None).ConfigureAwait(false);
#else
                    length = await clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer, 0, buffer.Length), SocketFlags.None).ConfigureAwait(false);
#endif
                }

                if (request.State != HttpRequestMessageState.MessageParsed)
                    throw new InvalidOperationException("Impossible suitation");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }
        private async ValueTask<Socket?> BuildSSLConnectionAsync(HttpRequestMessage request)
        {
            // TODO: Connection Pool

            var destination = request.RequestUri;

            if (IPAddress.TryParse(destination.DnsSafeHost, out var address))
                return await ConnectAsync(address, destination.Port).ConfigureAwait(false);

            foreach (var resolvedAddress in await Dns.GetHostAddressesAsync(destination.DnsSafeHost))
                try
                {
                    return await ConnectAsync(resolvedAddress, destination.Port).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }

            return null;

            static async ValueTask<Socket> ConnectAsync(IPAddress address, int port)
            {
                var result = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                await result.ConnectAsync(address, port).ConfigureAwait(false);

                return result;
            }
        }
        private async Task BuildTunnelAsync(Socket source, Socket destination)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(8192);

            try
            {
                while (true)
                {
#if NETSTANDARD2_1
                    var length = await source.ReceiveAsync(buffer.AsMemory(), SocketFlags.None).ConfigureAwait(false);
#else
                    var length = await source.ReceiveAsync(new ArraySegment<byte>(buffer, 0, buffer.Length), SocketFlags.None).ConfigureAwait(false);
#endif

                    if (length == 0)
                        return;

#if NETSTANDARD2_1
                    await destination.SendAsync(buffer.AsMemory(0, length), SocketFlags.None).ConfigureAwait(false);
#else

                    await destination.SendAsync(new ArraySegment<byte>(buffer, 0, length), SocketFlags.None).ConfigureAwait(false);
#endif
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }

        private async ValueTask SendRequestToRemoteHostAsync(HttpSession session)
        {
            Debug.Assert(session.Response == null);

            while (session.Response == null)
            {
                try
                {
                    using var request = session.Request.PrepareRequestMessageForHttpClient();
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                    session.Response = new HttpResponseMessage(response);

                    if (response.Content != null)
                    {
                    }
                }
                catch (HttpRequestException innerException)
                {
                }
            }
        }

        public void Stop()
        {
            var socket = Interlocked.Exchange(ref _listenerSocket, null);

            socket?.Dispose();
        }
    }
}
