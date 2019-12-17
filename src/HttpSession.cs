namespace Sakuno.Nekomimi
{
    public class HttpSession
    {
        public HttpRequestMessage Request { get; }
        public HttpResponseMessage? Response { get; set; }

        internal HttpSession()
        {
            Request = new HttpRequestMessage();
        }
    }
}
