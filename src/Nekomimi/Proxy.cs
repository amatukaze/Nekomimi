using System;
using System.Net;

namespace Sakuno.Nekomimi
{
    public class Proxy : IWebProxy
    {
        public Uri Uri { get; }

        public Proxy(string uriString) => Uri = new Uri(uriString);
        public Proxy(Uri uri) => Uri = uri;

        ICredentials IWebProxy.Credentials { get; set; }
        Uri IWebProxy.GetProxy(Uri destination) => Uri;
        bool IWebProxy.IsBypassed(Uri host) => false;
    }
}
