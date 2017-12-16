using Sakuno.Net;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    class Pipe
    {
        static ConcurrentQueue<SocketAsyncOperationContext> _operations;

        public Socket Socket { get; }

        public SocketAsyncOperationContext Operation { get; }

        public byte[] Buffer { get; }

        public PipeStream Stream { get; }

        static Pipe()
        {
            _operations = new ConcurrentQueue<SocketAsyncOperationContext>();

            for (var i = 0; i < 5; i++)
                _operations.Enqueue(new SocketAsyncOperationContext());
        }
        public Pipe(Socket socket)
        {
            Socket = socket;

            if (!_operations.TryDequeue(out var operation))
                operation = new SocketAsyncOperationContext();

            Operation = operation;

            Buffer = new byte[4096];

            Stream = new PipeStream(this);
        }

        public Task SendASCII(string content)
        {
            var length = Encoding.ASCII.GetBytes(content, 0, content.Length, Buffer, 0);

            return Stream.WriteAsync(Buffer, 0, length);
        }
        public Task Send(byte[] content) => Stream.WriteAsync(content, 0, content.Length);

        public async Task SendRequest(Session session)
        {
            await Send(HttpConstants.FromMethod(session.Method));
            await Send(HttpConstants.Whitespace);

            if (session.Method != HttpMethod.Connect)
                await SendASCII(session.Path);
            else
            {
                await SendASCII(session.Host);
                await SendASCII(":443");
            }

            await Send(HttpConstants.Whitespace);
            await Send(HttpConstants.FromVersion(session.HttpVersion));
            await Send(HttpConstants.CrLf);

            foreach (var header in session.RequestHeaders)
            {
                await Send(HttpConstants.FromHeaderName(header.Key));
                await Send(HttpConstants.Headers.Separator);
                await SendASCII(header.Value);
                await Send(HttpConstants.CrLf);
            }

            await Send(HttpConstants.CrLf);

            if (session.RequestBody != null)
                await Send(session.RequestBody);
        }

        public async Task SendResponse(Session session)
        {
            await Send(HttpConstants.FromVersion(session.HttpVersion));
            await Send(HttpConstants.Whitespace);

            await SendASCII(session.StatusCode.ToString());
            await Send(HttpConstants.Whitespace);
            await SendASCII(session.ReasonPhase);

            await Send(HttpConstants.CrLf);

            var chunkedEncoding = false;

            foreach (var header in session.ResponseHeaders)
            {
                await Send(HttpConstants.FromHeaderName(header.Key));
                await Send(HttpConstants.Headers.Separator);
                await SendASCII(header.Value);
                await Send(HttpConstants.CrLf);

                if (header.Key.OICEquals("Transfer-Encoding"))
                    chunkedEncoding = header.Value.OICContains("chunked");
            }

            await Send(HttpConstants.CrLf);

            if (session.ResponseBody != null)
            {
                if (chunkedEncoding)
                {
                    await SendASCII(session.ResponseBody.Length.ToString("x"));
                    await Send(HttpConstants.CrLf);
                }

                await Send(session.ResponseBody);

                if (chunkedEncoding)
                {
                    await Send(HttpConstants.CrLf);
                    await SendASCII("0");
                    await Send(HttpConstants.CrLf);
                    await Send(HttpConstants.CrLf);
                }
            }
        }

        public void Close()
        {
            Socket.Close();

            Operation.SetBuffer(0, 0);

            _operations.Enqueue(Operation);
        }
    }
}
