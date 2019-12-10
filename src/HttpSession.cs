namespace Sakuno.Nekomimi
{
    public class HttpSession
    {
        public HttpRequestMessage Request { get; }

        internal HttpSession()
        {
            Request = new HttpRequestMessage();
        }
    }
}
