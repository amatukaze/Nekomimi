using System;
using System.Diagnostics;
using System.Net.Http;

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

        private HttpHeaderCollection? _headers;
        public HttpHeaderCollection Headers
        {
            get
            {
                if (State <= HttpRequestMessageState.ParsingHeader)
                    throw new InvalidOperationException();

                Debug.Assert(_headers != null);

                return _headers;
            }
        }

        private byte[]? _body;
        public byte[] Body
        {
            get
            {
                if (State <= HttpRequestMessageState.ReadingBody)
                    throw new InvalidOperationException();

                Debug.Assert(_body != null);

                return _body;
            }
        }
    }
}
