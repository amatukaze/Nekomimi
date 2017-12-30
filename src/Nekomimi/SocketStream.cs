using Sakuno.Net;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    class SocketStream : Stream
    {
        volatile int _isDisposed;

        Socket _socket;
        SocketAsyncOperationContext _operation;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Length => throw new NotSupportedException();

        public SocketStream(Socket socket)
        {
            _socket = socket;
            _operation = new SocketAsyncOperationContext();
        }

        public async Task ConnectAsync(IPEndPoint remoteEndPoint)
        {
            _operation.RemoteEndPoint = remoteEndPoint;
            var result = await _socket.ConnectAsync(_operation);
            if (result != SocketError.Success)
                throw new SocketException((int)result);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_socket.Available == 0)
                while (!_socket.Poll(10, SelectMode.SelectRead)) { }
            if (_socket.Available == 0) return 0;
            return _socket.Receive(buffer, offset, count, SocketFlags.None);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _operation.SetBuffer(buffer, offset, count);

            if (_socket.Available == 0)
                try
                {
                    while (!await Task.Run(() => _socket.Poll(10, SelectMode.SelectRead))) { }
                }
                catch (SocketException)
                {
                    return 0;
                }
            if (_socket.Available == 0) return 0;
            var result = await _socket.ReceiveAsync(_operation);
            if (result != SocketError.Success)
                throw new SocketException((int)result);

            return _operation.BytesTransferred;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            _socket.Send(buffer, offset, count, SocketFlags.None);
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _operation.SetBuffer(buffer, offset, count);

            var result = await _socket.SendAsync(_operation);
            if (result != SocketError.Success)
                throw new SocketException((int)result);
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            if (_isDisposed != 0 || Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
                return;

            _socket.Disconnect(false);
            _socket.Dispose();
            _socket = null;
            _operation.SetBuffer(null);
            _operation = null;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
