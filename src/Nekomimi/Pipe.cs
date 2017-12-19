using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Sakuno.Nekomimi.IO;
using Sakuno.Net;

namespace Sakuno.Nekomimi
{
    internal class Pipe : IStreamWrapper
    {
        static ConcurrentQueue<SocketAsyncOperationContext> _operations;

        private int _readPosition, _readBufferLength;
        private bool _endOfStream;

        public Socket Socket { get; }

        public SocketAsyncOperationContext Operation { get; }

        private byte[] _buffer;

        private PipeStream _stream;

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

            _buffer = ArrayPool<byte>.Shared.Rent(4096);

            _stream = new PipeStream(this);
        }

        public byte ReadByte()
        {
            byte result = PeekByte();
            Advance();
            return result;
        }

        public byte PeekByte()
        {
            if (_endOfStream) return 0;
            if (_readBufferLength == 0) FillBuffer();
            return _buffer[_readPosition];
        }

        public void Advance()
        {
            if (_endOfStream) return;
            if (_readPosition >= _readBufferLength)
                FillBuffer();
            if (_readBufferLength > 0)
                _readPosition++;
        }

        private void FillBuffer()
        {
            if ((_readBufferLength = _stream.Read(_buffer, 0, _buffer.Length)) == 0)
                _endOfStream = true;
            _readPosition = 0;
        }

        public ValueTask<int> ReadAsync(ArraySegment<byte> buffer)
        {
            if (_readPosition < _readBufferLength)
            {
                int count = Math.Min(buffer.Count, _readBufferLength - _readPosition);
                Buffer.BlockCopy(_buffer, _readPosition, buffer.Array, buffer.Offset, buffer.Count);
                _readPosition += count;
                return new ValueTask<int>(count);
            }
            else
                return new ValueTask<int>(_stream.ReadAsync(buffer.Array, buffer.Offset, buffer.Count));
        }

        public Task SendASCII(string content)
        {
            var length = Encoding.ASCII.GetBytes(content, 0, content.Length, _buffer, 0);

            return _stream.WriteAsync(_buffer, 0, length);
        }
        public Task Send(byte[] content) => _stream.WriteAsync(content, 0, content.Length);

        public Task Send(Stream stream) => stream.CopyToAsync(_stream);

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

            await Send(new SegmentBufferStream(session.RequestBodyBuffer));
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

            if (chunkedEncoding)
            {
                await SendASCII((await session.ResponseBodyBuffer.FullFill()).ToString("x"));
                await Send(HttpConstants.CrLf);
            }

            await Send(new SegmentBufferStream(session.ResponseBodyBuffer));

            if (chunkedEncoding)
            {
                await Send(HttpConstants.CrLf);
                await SendASCII("0");
                await Send(HttpConstants.CrLf);
                await Send(HttpConstants.CrLf);
            }
        }

        public void Close()
        {
            Socket.Close();

            Operation.SetBuffer(0, 0);

            _operations.Enqueue(Operation);
            if (_buffer != null)
                ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}
