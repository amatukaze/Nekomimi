namespace Sakuno.Nekomimi
{
    public class Proxy
    {
        public string Host { get; }
        public int Port { get; }
        public Proxy(string host, int port)
        {
            Host = host;
            Port = port;
        }
    }
}
