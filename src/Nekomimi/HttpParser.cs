using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Sakuno.Nekomimi.IO;

namespace Sakuno.Nekomimi
{
    class HttpParser
    {
        Session _session;

        Pipe _pipe;

        StringBuilder _stringBuilder;

        public HttpParser(Session session, Pipe pipe)
        {
            _session = session;

            _pipe = pipe;

            _stringBuilder = new StringBuilder();
        }

        public void ParseRequest()
        {
            _session._method = ReadHttpMethod();

            AssertChar(' ');

            var requestUri = ReadUntilWhitespace(_stringBuilder);

            AssertChar(' ');

            _session.HttpVersion = ReadHttpVersion();

            AssertNewline();

            _session.RequestHeaders = ParseHeaders(_stringBuilder);

            AssertNewline();

            if (_session.RequestHeaders.TryGetValue("Host", out var host))
                _session._host = host;

            if (Uri.TryCreate(requestUri, UriKind.RelativeOrAbsolute, out var uri))
                _session._path = uri.LocalPath;

            if (_session._method == HttpMethod.Connect)
                _session._port = 443;
        }

        public void ReadRequestBody()
        {
            if (!_session.RequestHeaders.TryGetValue("Content-Length", out var lengthStr))
            {
                _session.RequestBodyBuffer = new SegmentBuffer(null, 0);
                return;
            }

            if (!long.TryParse(lengthStr, out var length))
                throw new FormatException();

            _session.RequestBodyBuffer = new SegmentBuffer(_pipe, length);
        }

        public void ParseResponse()
        {
            var version = ReadHttpVersion();

            AssertChar(' ');

            _session._statusCode = ReadDecimal();

            AssertChar(' ');

            _session.ReasonPhase = ReadUntilNewLine(_stringBuilder);

            AssertNewline();

            _session.ResponseHeaders = ParseHeaders(_stringBuilder);

            AssertNewline();
        }

        public void ReadResponseBody()
        {
            if (_session.ResponseHeaders.TryGetValue("Content-Length", out var lengthStr))
            {
                if (!long.TryParse(lengthStr, out var length))
                    throw new FormatException();

                _session.ResponseBodyBuffer = new SegmentBuffer(_pipe, length);
            }
            else if (_session.ResponseHeaders.TryGetValue("Transfer-Encoding", out var encoding) && encoding.OICContains("chunked"))
                _session.ResponseBodyBuffer = new SegmentBuffer(new ChunkedStream(this));
            else _session.ResponseBodyBuffer = new SegmentBuffer(null, 0);
        }

        HttpMethod ReadHttpMethod()
        {
            switch (_pipe.ReadByte())
            {
                case (byte)'G':
                case (byte)'g':
                    AssertLetter((byte)'E');
                    AssertLetter((byte)'T');

                    return HttpMethod.Get;

                case (byte)'H':
                case (byte)'h':
                    AssertLetter((byte)'E');
                    AssertLetter((byte)'A');
                    AssertLetter((byte)'D');

                    return HttpMethod.Head;

                case (byte)'D':
                case (byte)'d':
                    AssertLetter((byte)'E');
                    AssertLetter((byte)'L');
                    AssertLetter((byte)'E');
                    AssertLetter((byte)'T');
                    AssertLetter((byte)'E');

                    return HttpMethod.Delete;

                case (byte)'C':
                case (byte)'c':
                    AssertLetter((byte)'O');
                    AssertLetter((byte)'N');
                    AssertLetter((byte)'N');
                    AssertLetter((byte)'E');
                    AssertLetter((byte)'C');
                    AssertLetter((byte)'T');

                    return HttpMethod.Connect;

                case (byte)'O':
                case (byte)'o':
                    AssertLetter((byte)'P');
                    AssertLetter((byte)'T');
                    AssertLetter((byte)'I');
                    AssertLetter((byte)'O');
                    AssertLetter((byte)'N');
                    AssertLetter((byte)'S');

                    return HttpMethod.Options;

                case (byte)'T':
                case (byte)'t':
                    AssertLetter((byte)'R');
                    AssertLetter((byte)'A');
                    AssertLetter((byte)'C');
                    AssertLetter((byte)'E');

                    return HttpMethod.Trace;

                case (byte)'P':
                case (byte)'p':
                    switch (_pipe.ReadByte())
                    {
                        case (byte)'O':
                        case (byte)'o':
                            AssertLetter((byte)'S');
                            AssertLetter((byte)'T');

                            return HttpMethod.Post;

                        case (byte)'U':
                        case (byte)'u':
                            AssertLetter((byte)'T');

                            return HttpMethod.Put;

                        case (byte)'A':
                        case (byte)'a':
                            AssertLetter((byte)'T');
                            AssertLetter((byte)'C');
                            AssertLetter((byte)'H');

                            return HttpMethod.Patch;
                    }
                    break;
            }

            throw new FormatException("Unknown HTTP method");
        }
        void AssertLetter(byte b)
        {
            var current = _pipe.ReadByte();

            if (current == b)
                return;

            if (current - 0x20 == b)
                return;

            throw new FormatException("Expect " + (char)b);
        }

        void AssertChar(char c)
        {
            if (_pipe.ReadByte() != c)
                throw new FormatException("Expect " + c);
        }

        void AssertNewline()
        {
            AssertChar('\r');
            AssertChar('\n');
        }

        string ReadUntilWhitespace(StringBuilder builder)
        {
            builder.Clear();

            while (true)
            {
                var b = _pipe.PeekByte();
                if (b == ' ')
                    return builder.ToString();

                builder.Append((char)b);

                _pipe.Advance();
            }
        }
        string ReadUntilNewLine(StringBuilder builder)
        {
            builder.Clear();

            while (true)
            {
                var b = _pipe.PeekByte();
                if (b == '\r')
                    return builder.ToString();

                builder.Append((char)b);

                _pipe.Advance();
            }
        }

        HttpVersion ReadHttpVersion()
        {
            AssertChar('H');
            AssertChar('T');
            AssertChar('T');
            AssertChar('P');
            AssertChar('/');

            var major = _pipe.ReadByte();
            AssertDigit(major);

            major -= (byte)'0';

            AssertChar('.');

            var minor = _pipe.ReadByte();
            AssertDigit(minor);

            minor -= (byte)'0';

            return (HttpVersion)((major << 16) | minor);
        }

        bool IsDigit(byte b) => b >= '0' && b <= '9';
        void AssertDigit(byte b)
        {
            if (!IsDigit(b))
                throw new FormatException("Expect a digit");
        }

        IDictionary<string, string> ParseHeaders(StringBuilder builder)
        {
            var result = new SortedList<string, string>(StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                if (_pipe.PeekByte() == '\r')
                    break;

                builder.Clear();

                while (true)
                {
                    var b = _pipe.PeekByte();
                    if (!IsLetter(b) && b != '-')
                        break;

                    builder.Append((char)b);

                    _pipe.Advance();
                }

                AssertChar(':');

                var key = string.Intern(builder.ToString());

                if (_pipe.PeekByte() == ' ')
                {
                    _pipe.Advance();
                }

                builder.Clear();

                while (true)
                {
                    var b = _pipe.PeekByte();
                    if (b == '\r')
                        break;

                    builder.Append((char)b);

                    _pipe.Advance();
                }

                AssertNewline();

                var value = builder.ToString();

                result[key] = value;
            }

            return result;
        }

        bool IsLetter(byte b) => b >= 'A' && b <= 'Z' || (b >= 'a' && b <= 'z');

        int ReadDecimal()
        {
            var result = 0;

            while (true)
            {
                var b = _pipe.PeekByte();
                if (!IsDigit(b))
                    return result;

                result = result * 10 + b - '0';

                _pipe.Advance();
            }
        }

        int ReadHexadecimal()
        {
            var result = 0;

            while (true)
            {
                var b = _pipe.PeekByte();
                if (!IsCharInHexadecimal(b))
                    return result;

                result = result * 16;

                if (IsDigit(b))
                    result += b - '0';
                else if (b >= 'a')
                    result += b - 'a' + 10;
                else
                    result += b - 'A' + 10;

                _pipe.Advance();
            }
        }
        bool IsCharInHexadecimal(byte b) => b >= '0' && b <= '9' || (b >= 'A' && b <= 'F') || (b >= 'a' && b <= 'f');

        internal sealed class ChunkedStream : IStreamWrapper
        {
            private readonly HttpParser _parser;

            public ChunkedStream(HttpParser parser)
            {
                _parser = parser;
            }

            private int _chunkSize;
            private bool _endOfStream;
            public async ValueTask<int> ReadAsync(ArraySegment<byte> buffer)
            {
                if (_endOfStream) return 0;
                if (_chunkSize == 0)
                {
                    _chunkSize = _parser.ReadHexadecimal();
                    _parser.AssertNewline();
                    if (_chunkSize == 0)
                    {
                        _parser.AssertNewline();
                        _endOfStream = true;
                        return 0;
                    }
                }
                if (buffer.Count >= _chunkSize)
                {
                    int result = await _parser._pipe.ReadAsync(new ArraySegment<byte>(buffer.Array, buffer.Offset, _chunkSize));
                    _chunkSize = 0;
                    _parser.AssertNewline();
                    return result;
                }
                else
                {
                    int result = await _parser._pipe.ReadAsync(buffer);
                    _chunkSize -= result;
                    return result;
                }
            }
        }
    }
}
