using System.Net.Http;

namespace Sakuno.Nekomimi
{
    public class Session
    {
        public bool IsHTTPS { get; internal set; }

        public HttpRequestMessage Request { get; internal set; }

        public HttpResponseMessage Response { get; internal set; }
    }
}
