using Sakuno.Net;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    class SessionListerner
    {
        Socket _socket;

        SocketAsyncOperationContext _context;

        public bool IsListening { get; private set; }

        public SessionListerner()
        {
            _context = new SocketAsyncOperationContext();
        }

        public void Start(int port)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            _socket.Listen(1000);

            IsListening = true;
        }

        public void Stop()
        {
            IsListening = false;

            _socket.Close();
            _socket = null;
        }

        internal async Task<Session> GetNewSession()
        {
            await _socket.AcceptAsync(_context);

            var clientSocket = _context.AcceptSocket;

            _context.AcceptSocket = null;

            return new Session(clientSocket);
        }
    }
}
