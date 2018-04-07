using System;
using System.IO.Pipelines.Networking.Sockets;
using System.Net;
using System.Net.Http;

namespace Sakuno.Nekomimi
{
    public partial class ProxyServer : IDisposable
    {
        private readonly SocketListener _listener = new SocketListener();
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _httpClientHandler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        public ProxyServer()
        {
            _listener.OnConnection(HandleConnection);
            _httpClient = new HttpClient(_httpClientHandler);
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

        private IWebProxy _upstream;
        public IWebProxy UpstreamProxy
        {
            get => _upstream;
            set
            {
                _httpClientHandler.Proxy = _upstream = value;
                _httpClientHandler.UseProxy = _upstream != null;
            }
        }
    }
}
