using Sakuno.Net;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    public class Session
    {
        internal Pipe ServerPipe { get; }

        internal HttpMethod _method;
        public HttpMethod Method
        {
            get => _method;
            set
            {
                if (_method != value)
                    _method = value;
            }
        }

        internal HttpVersion HttpVersion { get; set; }

        internal string _host;
        public string Host
        {
            get => _host;
            set
            {
                if (_host != value)
                    _host = value;
            }
        }

        internal int _port = 80;
        public int Port
        {
            get => _port;
            set
            {
                if (_port != value)
                {
                    _port = value;
                }
            }
        }

        internal string _path;
        public string Path
        {
            get => _path;
            set
            {
                if (_path != value)
                    _path = value;
            }
        }

        public IDictionary<string, string> RequestHeaders { get; internal set; }

        public byte[] RequestBody { get; set; }

        IPEndPoint _forwardDestination;
        public IPEndPoint ForwardDestination
        {
            get => _forwardDestination;
            set
            {
                if (_forwardDestination != value)
                    _forwardDestination = value;
            }
        }

        internal Pipe ClientPipe { get; set; }

        internal int _statusCode;
        public int StatusCode
        {
            get => _statusCode;
            set
            {
                if (_statusCode != value)
                    _statusCode = value;
            }
        }

        internal string ReasonPhase { get; set; }

        public IDictionary<string, string> ResponseHeaders { get; internal set; }

        public byte[] ResponseBody { get; set; }

        public Session(Socket clientSocket)
        {
            ServerPipe = new Pipe(clientSocket);
        }

        internal async Task CreateClientPipeAndConnect(IPEndPoint remoteEndPoint)
        {
            var socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            ClientPipe = new Pipe(socket);
            ClientPipe.Operation.RemoteEndPoint = remoteEndPoint;

            await socket.ConnectAsync(ClientPipe.Operation);
        }
    }
}
