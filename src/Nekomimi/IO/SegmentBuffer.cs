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
        public long? Length { get; }

        private readonly SemaphoreSlim _streamLock = new SemaphoreSlim(0, 1);
        private readonly ReaderWriterLockSlim _listLock = new ReaderWriterLockSlim();

        public SegmentBuffer(Stream stream, ArraySegment<byte> usedBuffer, long? length)
        {
            _stream = stream;
            Length = length;
            _buffers.Add(usedBuffer);
        }

        protected override void DisposeNativeResources()
        {
            base.DisposeNativeResources();
            foreach (var buffer in _buffers)
                BufferPool.Return(buffer.Array);
        }

        public async ValueTask<int> Read(long position, byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            long pos = 0;
            int bytesRead = 0;
            int i = 0;
            while (i < _buffers.Count)
            {
                _listLock.EnterReadLock();
                var segment = _buffers[i];
                _listLock.ExitReadLock();
                if (segment.Count + pos >= position)
                {
                    int start = position > pos ? (int)(position - pos) : 0;
                    int available = segment.Count - start;
                    if (count <= available)
                    {
                        bytesRead += count;
                        Buffer.BlockCopy(segment.Array, segment.Offset + start, buffer, offset, count);
                        return bytesRead;
                    }
                    else
                    {
                        bytesRead += available;
                        Buffer.BlockCopy(segment.Array, segment.Offset + start, buffer, offset, available);
                        position += available;
                        offset += available;
                        count -= available;
                    }
                }
                if (++i == _buffers.Count && !_endOfStream)
                {
                    await _streamLock.WaitAsync();
                    try
                    {
                        if (_endOfStream) break;
                        if (i == _buffers.Count)
                        {
                            var newBuffer = BufferPool.Rent(BufferSize);
                            int bytesFromStream = await _stream.ReadAsync(newBuffer, 0, newBuffer.Length);
                            if (bytesFromStream > 0)
                            {
                                _listLock.EnterWriteLock();
                                _buffers.Add(new ArraySegment<byte>(newBuffer, 0, bytesFromStream));
                                _listLock.ExitWriteLock();
                            }
                            else
                                _endOfStream = true;
                        }
                    }
                    finally
                    {
                        _streamLock.Release();
                    }
                }
            }
            return bytesRead;
        }
    }
}
