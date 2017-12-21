using System;
using System.Net;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    public class ProxyServer : DisposableObject
    {
        SessionListerner _listener;

        public ProxyServer()
        {
            _listener = new SessionListerner();
        }

        public void Start(int port)
        {
            if (_listener.IsListening)
                throw new InvalidOperationException();

            _listener.Start(port);

            ListenerLoop();
        }

        public void Stop()
        {
            if (!_listener.IsListening)
                return;

            _listener.Stop();
        }

        protected override void DisposeManagedResources()
        {
            Stop();
        }

        public event Action<Session> BeforeRequest;
        public event Action<Session> AfterRequest;
        public event Action<Session> BeforeResponse;
        public event Action<Session> AfterResponse;

        async void ListenerLoop()
        {
            while (_listener.IsListening)
            {
                var session = await _listener.GetNewSession().ConfigureAwait(false);

                Task.Run(() =>
                {
                    try
                    {
                        HandleSession(session).Wait();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }).Forget();
            }
        }

        async Task HandleSession(Session session)
        {
            using (session.ServerPipe)
            {
                {
                    session.Status = SessionStatus.Preparing;
                    var parser = new HttpParser(session, session.ServerPipe);

                    parser.ParseRequest();

                    if (session.RequestHeaders.TryGetValue("Expect", out var expectHeaderValue) && expectHeaderValue.OICEquals("100-continue"))
                    {
                        await session.ServerPipe.SendASCII("100 Continue");
                    }

                    parser.ReadRequestBody();
                    session.Status = SessionStatus.BeforeRequest;
                    BeforeRequest?.Invoke(session);
                }

                {
                    var remoteEndPoint = session.ForwardDestination;
                    if (remoteEndPoint == null)
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(session.Host);

                        remoteEndPoint = new IPEndPoint(hostEntry.AddressList[0], session.Port);
                    }

                    await session.CreateClientPipeAndConnect(remoteEndPoint);
                }

                using (session.ClientPipe)
                {
                    await session.ClientPipe.SendRequest(session);

                    session.Status = SessionStatus.AfterRequest;
                    AfterRequest?.Invoke(session);

                    var parser = new HttpParser(session, session.ClientPipe);

                    parser.ParseResponse();

                    parser.ReadResponseBody();

                    session.Status = SessionStatus.BeforeResponse;
                    BeforeResponse?.Invoke(session);

                    await session.ServerPipe.SendResponse(session);

                    session.Status = SessionStatus.AfterResponse;
                    AfterResponse?.Invoke(session);
                }
            }
            session.Status = SessionStatus.Completed;
        }
    }
}
