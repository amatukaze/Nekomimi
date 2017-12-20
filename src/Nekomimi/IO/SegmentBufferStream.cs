using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi.IO
{
    internal class SegmentBufferStream : Stream
    {
        private readonly SegmentBuffer _buffer;
        private long _position;

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

        private int _currentIndex, _currentSegmentOffset, _bytesRead, _offset, _count;
        private long _currentSegmentStart;
        private ArraySegment<byte> _currentSegment, _dest;
        public async ValueTask<int> ReadAsync(ArraySegment<byte> buffer)
        {
            if (_currentSegmentStart > Position)
            {
                _currentSegment = default;
                _currentSegmentStart = 0;
                _currentIndex = -1;
            }
            if (_currentIndex == -1 && !await NextSegmentAsync())
                return 0;
            if (_currentSegment == default) return 0;
            _bytesRead = 0;
            _offset = buffer.Offset;
            _count = buffer.Count;
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
