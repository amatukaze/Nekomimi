using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Sakuno.Net;

namespace Sakuno.Nekomimi
{
    internal class Pipe : IStreamWrapper, IDisposable
    {
        private int _readPosition, _readBufferLength;
        private bool _endOfStream;

        private byte[] _buffer;

        private Socket _socket;
        private Stream _stream;

        public Pipe(Socket socket)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(4096);

            socket.Blocking = true;
            _socket = socket;
            _stream = new SocketStream(socket);
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
                Buffer.BlockCopy(_buffer, _readPosition, buffer.Array, buffer.Offset, count);
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

            if (session.RequestBodyBuffer != null)
                await Send(session.RequestBodyBuffer.CreateStream());
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

            if (session.ResponseBodyBuffer != null)
                await Send(session.ResponseBodyBuffer.CreateStream());

            if (chunkedEncoding)
            {
                await Send(HttpConstants.CrLf);
                await SendASCII("0");
                await Send(HttpConstants.CrLf);
                await Send(HttpConstants.CrLf);
            }
        }

        public async Task TunnelTo(Pipe other)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            var context = new SocketAsyncOperationContext();
            context.SetBuffer(buffer);
            try
            {
                bool clientSent = false;
                while (true)
                {
                    if (_socket.Available > 0)
                    {
                        var receiveResult = await _socket.ReceiveAsync(context);
                        if (receiveResult != SocketError.Success) return;

                        context.SetBuffer(0, context.BytesTransferred);

                        var sendResult = await other._socket.SendAsync(context);
                        if (sendResult != SocketError.Success) return;

                        clientSent = true;
                    }
                    else if (other._socket.Available > 0)
                    {
                        var receiveResult = await other._socket.ReceiveAsync(context);
                        if (receiveResult != SocketError.Success) return;

                        context.SetBuffer(0, context.BytesTransferred);

                        var sendResult = await _socket.SendAsync(context);
                        if (sendResult != SocketError.Success) return;

                        clientSent = true;
                    }
                    else if (clientSent)
                    {
                        if (other._socket.Poll(10, SelectMode.SelectRead) && other._socket.Available == 0) return;
                    }
                    else
                    {
                        if (_socket.Poll(10, SelectMode.SelectRead) && _socket.Available == 0) return;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        void IDisposable.Dispose()
        {
            _stream.Dispose();
            if (_buffer != null)
                ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}
