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
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _buffer.Read(Position, buffer, offset, count).Result;
            Position += bytesRead;
            return bytesRead;
        }

        public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _buffer.Read(Position, buffer, offset, count);
            Position += bytesRead;
            return bytesRead;
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
