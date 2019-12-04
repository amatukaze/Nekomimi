using System;
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
        }

        public void Stop()
        {
            var socket = Interlocked.Exchange(ref _listenerSocket, null);

            socket?.Dispose();
        }
    }
}
