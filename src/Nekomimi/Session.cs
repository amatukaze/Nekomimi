using System.Net.Http;

namespace Sakuno.Nekomimi
{
    public class Session
    {
        public HttpRequestMessage Request { get; } = new HttpRequestMessage();
    }
}
