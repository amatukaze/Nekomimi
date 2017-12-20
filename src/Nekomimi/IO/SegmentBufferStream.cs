using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi.IO
{
    internal class SegmentBufferStream : Stream
    {
        private readonly SegmentBuffer _buffer;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _buffer.Length ?? throw new NotSupportedException();
        public override long Position { get; set; }

        public SegmentBufferStream(SegmentBuffer buffer)
        {
            _buffer = buffer;
            _currentIndex = -1;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(new ArraySegment<byte>(buffer, offset, count)).Result;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(new ArraySegment<byte>(buffer, offset, count)).AsTask();

        public async override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (!await CheckPositionAsync()) return;
            do
            {
                await destination.WriteAsync(_currentSegment.Array, _currentSegment.Offset + _currentSegmentOffset, _currentSegment.Count - _currentSegmentOffset);
                Position += _currentSegment.Count - _currentSegmentOffset;
            }
            while (await NextSegmentAsync());
        }

        private int _currentIndex, _currentSegmentOffset;
        private long _currentSegmentStart;
        private ArraySegment<byte> _currentSegment;
        public async ValueTask<int> ReadAsync(ArraySegment<byte> buffer)
        {
            if (!await CheckPositionAsync())
                return 0;
            int _bytesRead = 0;
            int _offset = buffer.Offset;
            int _count = buffer.Count;
            while (_count > 0)
            {
                int available = _currentSegment.Count - _currentSegmentOffset;
                if (available > _count)
                {
                    Buffer.BlockCopy(_currentSegment.Array, _currentSegmentOffset, buffer.Array, _offset, _count);
                    _currentSegmentOffset += _count;
                    Position += _count;
                    return _bytesRead + _count;
                }
                Buffer.BlockCopy(_currentSegment.Array, _currentSegmentOffset, buffer.Array, _offset, available);
                _offset += available;
                _count -= available;
                _bytesRead += available;
                Position += available;
                if (!await NextSegmentAsync()) break;
            }
            return _bytesRead;
        }

        private async ValueTask<bool> CheckPositionAsync()
        {
            if (_currentSegmentStart > Position)
            {
                _currentSegment = default;
                _currentSegmentStart = 0;
                _currentIndex = -1;
            }
            while (_currentSegmentStart + _currentSegment.Count <= Position)
            {
                if (!await NextSegmentAsync()) return false;
            }
            return _currentSegment != default;
        }

        private async ValueTask<bool> NextSegmentAsync()
        {
            _currentSegmentOffset = 0;
            if (++_currentIndex < _buffer.SegmentCount)
            {
                _currentSegmentStart += _currentSegment.Count;
                _currentSegment = _buffer[_currentIndex];
                return true;
            }
            int newCount = await _buffer.CheckNewSegmentAsync(_currentIndex);
            if (_currentIndex < newCount)
            {
                _currentSegmentStart += _currentSegment.Count;
                _currentSegment = _buffer[_currentIndex];
                return true;
            }
            else
            {
                _currentSegment = default;
                return false;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    if (offset < 0 || offset >= _buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
                    Position = offset;
                    return offset;
                case SeekOrigin.Current:
                    if (offset + Position < 0 || offset + Position >= _buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
                    Position += offset;
                    return Position;
                case SeekOrigin.End:
                    if (offset + Length < 0 || offset >= 0) throw new ArgumentOutOfRangeException(nameof(offset));
                    Position = offset + Length;
                    return Position;
                default:
                    throw new ArgumentException(nameof(origin));
            }
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
