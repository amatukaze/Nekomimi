using FxHttpResponseMessage = System.Net.Http.HttpResponseMessage;

namespace Sakuno.Nekomimi
{
    public sealed class HttpResponseMessage
    {
        public HttpVersion Version { get; }

        public int StatusCode { get; }
        public string ReasonPhrase { get; }

        public HttpHeaderCollection Headers { get; }

        public HttpResponseMessage(FxHttpResponseMessage response)
        {
            Version = response.Version.AsHttpVersion();

            StatusCode = (int)response.StatusCode;
            ReasonPhrase = response.ReasonPhrase;

            Headers = new HttpHeaderCollection();
            foreach (var (name, values) in response.Headers)
                foreach (var value in values)
                    Headers.Add(name, value);
        }
    }
}
