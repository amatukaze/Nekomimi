﻿using Sakuno.Net;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    class PipeStream : Stream
    {
        volatile int _isDisposed;

        Socket _socket;
        SocketAsyncOperationContext _operation;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Length => throw new NotSupportedException();

        public PipeStream(Pipe pipe)
        {
            _socket = pipe.Socket;
            _operation = pipe.Operation;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _socket.Receive(buffer, offset, count, SocketFlags.None);
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _operation.SetBuffer(buffer, offset, count);

            var result = await _socket.ReceiveAsync(_operation);

            return _operation.BytesTransferred;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _socket.Send(buffer, offset, count, SocketFlags.None);
        }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _operation.SetBuffer(buffer, offset, count);

            var result = await _socket.SendAsync(_operation);
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            if (_isDisposed != 0 || Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
                return;

            _socket = null;
            _operation = null;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
