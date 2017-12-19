using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi.IO
{
    internal sealed class SegmentBuffer : DisposableObject
    {
        internal static ArrayPool<byte> BufferPool { get; } = ArrayPool<byte>.Create();
        internal const int BufferSize = 4096;

        private readonly List<ArraySegment<byte>> _buffers = new List<ArraySegment<byte>>();
        private readonly Stream _stream;
        private bool _endOfStream;
        public long? Length { get; private set; }
        private long _bufferedLength;

        private readonly SemaphoreSlim _streamLock = new SemaphoreSlim(0, 1);
        private readonly ReaderWriterLockSlim _listLock = new ReaderWriterLockSlim();

        public SegmentBuffer(Stream stream, ArraySegment<byte> usedBuffer = default, long? length = null)
        {
            _stream = stream;
            Length = length;
            if (usedBuffer != default)
            {
                _bufferedLength = usedBuffer.Count;
                _buffers.Add(usedBuffer);
            }
        }

        protected override void DisposeNativeResources()
        {
            base.DisposeNativeResources();
            foreach (var buffer in _buffers)
                BufferPool.Return(buffer.Array);
        }

        public int SegmentCount
        {
            get
            {
                ThrowIfDisposed();
                return _buffers.Count;
            }
        }

        public ArraySegment<byte> this[int index]
        {
            get
            {
                ThrowIfDisposed();
                _listLock.EnterReadLock();
                var segment = _buffers[index];
                _listLock.ExitReadLock();
                return segment;
            }
        }

        public async ValueTask<int> CheckNewSegmentAsync(int oldCount)
        {
            if (_buffers.Count > oldCount || _endOfStream) return _buffers.Count;
            await _streamLock.WaitAsync();
            try
            {
                if (_buffers.Count == oldCount && !_endOfStream)
                {
                    var newBuffer = BufferPool.Rent(BufferSize);
                    int bytesFromStream = await _stream.ReadAsync(newBuffer, 0, newBuffer.Length);
                    if (bytesFromStream > 0)
                    {
                        _listLock.EnterWriteLock();
                        _buffers.Add(new ArraySegment<byte>(newBuffer, 0, bytesFromStream));
                        _bufferedLength += bytesFromStream;
                        _listLock.ExitWriteLock();
                    }
                    else
                    {
                        _endOfStream = true;
                        Length = _bufferedLength;
                    }
                }
                return _buffers.Count;
            }
            finally
            {
                _streamLock.Release();
            }
        }
    }
}
