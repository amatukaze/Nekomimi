using System;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi.Sample.Basic
{
    static class Program
    {
        static TaskCompletionSource<object> _completion;

        static ProxyServer _proxyServer;

        static void Main(string[] args)
        {
            _completion = new TaskCompletionSource<object>();

            Console.CancelKeyPress += Console_CancelKeyPress;

            _proxyServer = new ProxyServer();

            _proxyServer.Start(15000);

            _proxyServer.AfterResponse += session =>
                Console.WriteLine($"{session.Request.Method} {session.Request.RequestUri} {(int)session.Response.StatusCode} {session.Response.ReasonPhrase}");

            Console.WriteLine("Press Ctrl+C to stop...");

            _completion.Task.Wait();

            _proxyServer.Stop();
        }

        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            _completion.TrySetResult(null);
        }
    }
}
