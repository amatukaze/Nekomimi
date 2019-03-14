using System;
using System.IO.Pipelines.Networking.Sockets;
using System.Net;
using System.Net.Http;

namespace Sakuno.Nekomimi
{
    public partial class ProxyServer : IDisposable
    {
        private readonly SocketListener _listener = new SocketListener();
        private HttpClient _httpClient;
        public bool IsStarted { get; private set; }

        public ProxyServer()
        {
            _listener.OnConnection(HandleConnection);
        }

        public void Start(int port)
        {
            IsStarted = true;
            _httpClient = new HttpClient(new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                Proxy = UpstreamProxy,
                UseProxy = UpstreamProxy != null
            });
            _listener.Start(new IPEndPoint(IPAddress.Loopback, port));
        }

        public void Stop()
        {
            _listener.Stop();
            _httpClient?.Dispose();
            IsStarted = false;
        }

        public void Dispose()
        {
            _listener.Dispose();
            _httpClient?.Dispose();
        }

        public event Action<Session> BeforeRequest;
        public event Action<Session> AfterRequest;
        public event Action<Session> BeforeResponse;
        public event Action<Session, ReadOnlyMemory<byte>> DataReceiving;
        public event Action<Session> AfterResponse;
        public event Action<Session, Exception> SessionFailed;
        //public event Action<Session, long> SessionProgress;
        public event Action<Session> SslConnecting;

        private void EatException(Action<Session> handler, Session session)
        {
            try
            {
                handler?.Invoke(session);
            }
            catch { }
        }
        private void EatException(Action<Session, ReadOnlyMemory<byte>> handler, Session session, ReadOnlyMemory<byte> memory)
        {
            try
            {
                handler?.Invoke(session, memory);
            }
            catch { }
        }

        private IWebProxy _upstream;
        public IWebProxy UpstreamProxy
        {
            get => _upstream;
            set => _upstream = IsStarted ? throw new InvalidOperationException("Proxy cannot be changed during listening.") : value;
        }
    }
}
