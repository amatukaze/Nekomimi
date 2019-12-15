using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace Sakuno.Nekomimi
{
    public sealed partial class HttpRequestMessage
    {
        private const byte ByteSpace = 0x20;
        private const byte ByteCr = 0x0D;
        private const byte ByteLf = 0x0A;

        internal HttpRequestMessageState State { get; set; }

        private Memory<byte> _readingBody;

        internal bool ParseStartLineAndHeaders(ReadOnlySpan<byte> data, out int consumed)
        {
            consumed = 0;

            while (State != HttpRequestMessageState.HeadersParsed)
            {
                var currentConsumed = State switch
                {
                    HttpRequestMessageState.ParsingMethod => ParseMethod(data),
                    HttpRequestMessageState.ParsingRequestUri => ParseRequestUri(data),
                    HttpRequestMessageState.ParsingVersion => ParseVersion(data),
                    HttpRequestMessageState.ParsingHeader => ParseHeader(data),

                    _ => throw new InvalidOperationException(),
                };

                if (currentConsumed == 0)
                    return false;

                if (State < HttpRequestMessageState.ParsingHeader)
                    State++;

                consumed += currentConsumed;
                data = data.Slice(currentConsumed);
            }

            Debug.Assert(_headers != null);

            if (_headers.TryGetValue("Expect", out var expect) && expect == "100-continue")
                State = HttpRequestMessageState.HandlingExpect100Continue;
            else
                State = HttpRequestMessageState.ReadingBody;

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
                return 0;

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
                return 0;

            var second4Bytes = data.Slice(4).ReadInt();
            switch (second4Bytes)
            {
                case 0x20534E4F:
                    _method = HttpMethod.Options;
                    return 8;

                case 0x20544345:
                    _method = HttpMethod.Connect;
                    IsHttps = true;
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

            var requestUri = data.Slice(0, result).GetAsciiString();
            if (IsHttps)
                requestUri = "https://" + requestUri;

            _requestUri = new Uri(requestUri);

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

            _headers ??= new HttpHeaderCollection();

            if (data.ReadShort() == 0x0A0D)
            {
                State = HttpRequestMessageState.HeadersParsed;
                return 2;
            }

            var offset = data.IndexOf(new[] { ByteCr, ByteLf });
            if (offset == -1)
                return 0;

            var nameLength = data.IndexOf((byte)':');
            if (nameLength == -1)
                return 0;

            var name = data.Slice(0, nameLength);

            var valueOffset = nameLength + 1;
            if (data[valueOffset] == ByteSpace)
                valueOffset++;

            var value = data.Slice(valueOffset, offset - valueOffset);

            _headers.Add(name.GetAsciiString(), value.GetAsciiString());

            return offset + 2;
        }

        internal int ReadBody(ReadOnlySpan<byte> data)
        {
            if (_body == null)
            {
                Debug.Assert(_headers != null);

                if (!_headers.TryGetValue("Content-Length", out var value))
                    return 0;

                if (!int.TryParse(value, out var contentLength))
                    throw new BadRequestException();

                var body = new byte[contentLength];

                _readingBody = body;
                _body = new ByteArrayContent(body);
            }

            if (_readingBody.IsEmpty)
                return 0;

            var length = Math.Min(_readingBody.Length, data.Length);

            data.Slice(0, length).CopyTo(_readingBody.Span);
            _readingBody = _readingBody.Slice(length);

            return _readingBody.Length;
        }
    }
}
