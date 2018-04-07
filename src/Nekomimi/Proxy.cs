using System;
using System.Net;

namespace Sakuno.Nekomimi
{
    public class Proxy : IWebProxy
    {
        public Uri Uri { get; }

        public Proxy(string uriString) : this(new Uri(uriString)) { }
        public Proxy(Uri uri)
        {
            if (uri.HostNameType == UriHostNameType.Unknown)
                throw new FormatException("Invalid proxy uri.");
            Uri = uri;
        }

        ICredentials IWebProxy.Credentials { get; set; }
        Uri IWebProxy.GetProxy(Uri destination) => Uri;
        bool IWebProxy.IsBypassed(Uri host) => false;
    }
}
