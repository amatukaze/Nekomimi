using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
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
        public event Action<Session, Exception> SessionFailed;
        public event Action<Session, long> SessionProgress;
        public event Action<Session> SslConnecting;

        public Proxy UpstreamProxy { get; set; }

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
                        throw;
                    }
                }).Forget();
            }
        }

        async Task HandleSession(Session session)
        {
            using (session.ServerPipe)
            {
                session.Status = SessionStatus.Preparing;

                try
                {
                    var parser = new HttpParser(session, session.ServerPipe);

                    parser.ParseRequest();

                    if (session.RequestHeaders.TryGetValue("Expect", out var expectHeaderValue) && expectHeaderValue.OICEquals("100-continue"))
                    {
                        await session.ServerPipe.SendASCII("100 Continue");
                        session.RequestHeaders.Remove("Expect");
                    }

                    parser.ReadRequestBody();
                }
                catch (Exception e)
                {
                    SessionFailed?.Invoke(session, e);
                    return;
                }

                if (session.IsHTTPS)
                {
                    SslConnecting?.Invoke(session);
                }
                else
                {
                    session.Status = SessionStatus.BeforeRequest;
                    BeforeRequest?.Invoke(session);
                }

                var proxy = UpstreamProxy;
                try
                {
                    string host = proxy?.Host ?? session.Host;
                    int port = proxy?.Port ?? session.Port;

                    var hostEntry = await Dns.GetHostEntryAsync(host);
                    IPEndPoint selectedEndPoint = null;
                    List<Exception> exceptions = null;

                    foreach (var address in hostEntry.AddressList)
                    {
                        var remoteEndPoint = new IPEndPoint(address, port);
                        try
                        {
                            await session.CreateClientPipeAndConnect(remoteEndPoint);

                            selectedEndPoint = remoteEndPoint;
                            if (proxy != null)
                                session.ForwardDestination = remoteEndPoint;

                            break;
                        }
                        catch (SocketException e)
                        {
                            if (exceptions == null)
                                exceptions = new List<Exception> { e };
                            else exceptions.Add(e);
                        }
                    }

                    if (selectedEndPoint == null)
                    {
                        if (exceptions.Count == 1)
                        {
                            var ex = exceptions[0];
                            var info = ExceptionDispatchInfo.Capture(ex);
                            info.Throw();
                        }
                        else
                            throw new AggregateException(exceptions);
                    }
                }
                catch (Exception e)
                {
                    SessionFailed?.Invoke(session, e);
                    return;
                }

                using (session.ClientPipe)
                {
                    if (session.IsHTTPS)
                    {
                        if (session.ForwardDestination != null)
                        {
                            await session.ClientPipe.SendRequest(session);
                            var parser = new HttpParser(session, session.ClientPipe);
                            parser.ParseResponse();
                        }
                        else
                        {
                            session.StatusCode = 200;
                            session.ReasonPhase = "Connection Established";
                            session.ResponseHeaders = new SortedList<string, string>();
                        }
                        await session.ServerPipe.SendResponse(session);
                        await session.ServerPipe.TunnelTo(session.ClientPipe);
                    }
                    else
                    {
                        try
                        {
                            await session.ClientPipe.SendRequest(session);
                        }
                        catch (Exception e)
                        {
                            SessionFailed?.Invoke(session, e);
                            return;
                        }

                        session.Status = SessionStatus.AfterRequest;
                        AfterRequest?.Invoke(session);

                        try
                        {
                            var parser = new HttpParser(session, session.ClientPipe);

                            parser.ParseResponse();
                            parser.ReadResponseBody();

                            session.ResponseBodyBuffer.ProgressChanged += progress => SessionProgress?.Invoke(session, progress);
                        }
                        catch (Exception e)
                        {
                            SessionFailed?.Invoke(session, e);
                            return;
                        }

                        session.Status = SessionStatus.BeforeResponse;
                        BeforeResponse?.Invoke(session);

                        try
                        {
                            await session.ServerPipe.SendResponse(session);
                        }
                        catch (Exception e)
                        {
                            SessionFailed?.Invoke(session, e);
                            return;
                        }

                        session.Status = SessionStatus.AfterResponse;
                        AfterResponse?.Invoke(session);
                    }
                }
            }
            session.Status = SessionStatus.Completed;
        }
    }
}
