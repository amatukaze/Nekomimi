using System;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi.Sample.Basic
{
    internal static class Program
    {
        private static TaskCompletionSource<object> _completion;
        private static ProxyServer _proxyServer;

        private static async Task Main(string[] args)
        {
            _completion = new TaskCompletionSource<object>();

            Console.CancelKeyPress += Console_CancelKeyPress;

            _proxyServer = new ProxyServer();

            _proxyServer.Start(15000);

            _proxyServer.AfterResponse += session =>
                Console.WriteLine($"{session.Request.Method} {session.Request.RequestUri} {(int)session.Response.StatusCode} {session.Response.ReasonPhrase}");

            Console.WriteLine("Press Ctrl+C to stop...");

            await _completion.Task;

            _proxyServer.Stop();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            _completion.TrySetResult(null);
        }
    }
}
