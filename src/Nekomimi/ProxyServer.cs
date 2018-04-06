using System;
using System.IO.Pipelines.Networking.Sockets;
using System.Net;
using System.Net.Http;

namespace Sakuno.Nekomimi
{
    public partial class ProxyServer : IDisposable
    {
        private readonly SocketListener _listener = new SocketListener();
        private readonly HttpClient _httpClient = new HttpClient();
        public ProxyServer()
        {
            _listener.OnConnection(HandleConnection);
            _httpClient.DefaultRequestHeaders.Clear();
        }

        public void Start(int port)
        {
            _listener.Start(new IPEndPoint(IPAddress.Loopback, port));
        }

        public void Stop() => _listener.Stop();

        public void Dispose()
        {
            _listener.Dispose();
            _httpClient.Dispose();
        }

        public event Action<Session> BeforeRequest;
        public event Action<Session> AfterRequest;
        public event Action<Session> BeforeResponse;
        public event Action<Session> AfterResponse;
        public event Action<Session, Exception> SessionFailed;
        public event Action<Session, long> SessionProgress;
        public event Action<Session> SslConnecting;

        public Proxy UpstreamProxy { get; set; }
    }
}
