using System;
using System.Diagnostics;

namespace Sakuno.Nekomimi
{
    public sealed partial class HttpRequestMessage
    {
        private HttpMethod _method;
        public HttpMethod Method
        {
            get
            {
                if (State <= HttpRequestMessageState.ParsingMethod)
                    throw new InvalidOperationException();

                return _method;
            }
        }

        private Uri? _requestUri;
        public Uri RequestUri
        {
            get
            {
                if (State <= HttpRequestMessageState.ParsingRequestUri)
                    throw new InvalidOperationException();

                Debug.Assert(_requestUri != null);

                return _requestUri;
            }
        }

        private HttpVersion _version;
        public HttpVersion Version
        {
            get
            {
                if (State <= HttpRequestMessageState.ParsingVersion)
                    throw new InvalidOperationException();

                return _version;
            }
        }

        public bool IsHttps { get; internal set; }
    }
}
