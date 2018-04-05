using System;

namespace Sakuno.Nekomimi
{
    public class ProxyServer : IDisposable
    {
        public void Start(int port)
        {

        }

        public void Stop()
        {

        }

        public void Dispose() => Stop();

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
