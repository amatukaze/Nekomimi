using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Nekomimi
{
    public class ProxyServer
    {
        private Socket? _listenerSocket;

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

            await HandleRequestMessage(session.Request, clientSocket);

        }
        private async ValueTask HandleRequestMessage(HttpRequestMessage request, Socket clientSocket)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(4096);

            try
            {
                var offset = 0;

                while (true)
                {
#if NETSTANDARD2_1
                    var length = await clientSocket.ReceiveAsync(buffer.AsMemory(offset), SocketFlags.None).ConfigureAwait(false);
#else
                    var length = await clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, buffer.Length - offset), SocketFlags.None).ConfigureAwait(false);
#endif
                    if (request.ParseStartLineAndHeaders(buffer.AsSpan(0, length + offset), out var consumed))
                        break;

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
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }

        public void Stop()
        {
            var socket = Interlocked.Exchange(ref _listenerSocket, null);

            socket?.Dispose();
        }
    }
}
