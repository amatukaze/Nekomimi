﻿using System;

namespace Nekomimi
{
    public sealed partial class HttpRequestMessage
    {
        private const byte ByteSpace = 0x20;
        private const byte ByteCr = 0x0D;
        private const byte ByteLf = 0x0A;

        private enum State
        {
            Method,
            RequestUri,
            Version,
            Header,
            HeaderEnd,
            Body,

            End,
        }

        private State _state;

        internal bool Parse(ReadOnlySpan<byte> data, out int consumed)
        {
            consumed = 0;

            while (_state != State.End)
            {
                var currentConsumed = _state switch
                {
                    State.Method => ParseMethod(data),
                    State.RequestUri => ParseRequestUri(data),
                    State.Version => ParseVersion(data),
                    State.Header => ParseHeader(data),
                    State.Body => ParseBody(data),

                    _ => throw new InvalidOperationException(),
                };

                if (currentConsumed == 0)
                    return false;

                if (_state != State.Header)
                    _state++;

                consumed += currentConsumed;
                data = data.Slice(currentConsumed);
            }

            return true;
        }

        private int ParseMethod(ReadOnlySpan<byte> data)
        {
            var result = ParseKnownMethod(data);
            if (result > 0)
                return result;

            throw new NotImplementedException();
        }
        private int ParseKnownMethod(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
                return default;

            var first4Bytes = data.ReadInt();
            switch (first4Bytes)
            {
                case 0x20544547:
                    _method = HttpMethod.Get;
                    return 4;

                case 0x20545550:
                    _method = HttpMethod.Put;
                    return 4;
            }

            if (data.Length < 8)
                return default;

            var second4Bytes = data.Slice(4).ReadInt();
            switch (second4Bytes)
            {
                case 0x20534E4F:
                    _method = HttpMethod.Options;
                    return 8;

                case 0x20544345:
                    _method = HttpMethod.Connect;
                    return 8;
            }

            if ((second4Bytes & 0xFF) == ByteSpace)
            {
                switch (first4Bytes)
                {
                    case 0x54534F50:
                        _method = HttpMethod.Post;
                        return 5;

                    case 0x44414548:
                        _method = HttpMethod.Head;
                        return 5;
                }
            }

            var masked2 = second4Bytes & 0xFFFF;
            switch (first4Bytes)
            {
                case 0x43415254 when masked2 == 0x2045:
                    _method = HttpMethod.Trace;
                    return 6;

                case 0x43544150 when masked2 == 0x2048:
                    _method = HttpMethod.Patch;
                    return 6;
            }

            var masked3 = second4Bytes & 0xFFFFFF;
            if (first4Bytes == 0x454C4544 && masked3 == 0x204554)
            {
                _method = HttpMethod.Delete;
                return 7;
            }

            return 0;
        }

        private int ParseRequestUri(ReadOnlySpan<byte> data)
        {
            var result = data.IndexOf(ByteSpace);
            if (result == 0)
                throw new BadRequestException();

            return result + 1;
        }

        private int ParseVersion(ReadOnlySpan<byte> data)
        {
            if (data.Length < 10)
                return 0;

            if (data[8] != ByteCr || data[9] != ByteLf)
                throw new BadRequestException();

            _version = data.ReadLong() switch
            {
                0x302E312F50545448 => HttpVersion.Http10,
                0x312E312F50545448 => HttpVersion.Http11,

                _ => throw new BadRequestException(),
            };

            return 10;
        }

        private int ParseHeader(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2)
                return 0;

            if (data.ReadShort() == 0x0A0D)
            {
                _state = State.HeaderEnd;
                return 2;
            }

            var offset = data.IndexOf(new[] { ByteCr, ByteLf });
            if (offset == -1)
                return 0;

            return offset + 2;
        }

        private int ParseBody(ReadOnlySpan<byte> data)
        {
            return 0;
        }
    }
}
