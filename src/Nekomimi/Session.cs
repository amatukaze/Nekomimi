using System;
using System.Net.Http;

namespace Sakuno.Nekomimi
{
    public class Session
    {
        public bool IsHTTPS { get; internal set; }

        public bool RequestSent { get; internal set; }
        public bool ResponseSent { get; internal set; }

        private HttpRequestMessage _request;
        public HttpRequestMessage Request
        {
            get => _request;
            set => _request = RequestSent ?
                throw new InvalidOperationException("Cannot override request after sent.") :
                value;
        }

        private HttpResponseMessage _response;
        public HttpResponseMessage Response
        {
            get => IsHTTPS ?
                throw new InvalidOperationException("Cannot decrypt HTTPS session.") :
                _response;
            set => _response = ResponseSent ?
                throw new InvalidOperationException("Cannot override response after sent.") :
                value;
        }

        public object UserData { get; set; }
    }
}
