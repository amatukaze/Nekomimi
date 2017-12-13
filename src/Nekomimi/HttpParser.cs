using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Sakuno.Nekomimi
{
    class HttpParser
    {
        Session _session;

        Pipe _pipe;

        int _length, _currentPosition;

        byte? _singleByte;

        public byte CurrentByte => _singleByte == null ? _pipe.Buffer[_currentPosition] : _singleByte.Value;

        StringBuilder _stringBuilder;

        public HttpParser(Session session, Pipe pipe)
        {
            _session = session;

            _pipe = pipe;

            _stringBuilder = new StringBuilder();
        }

        byte ReadByte()
        {
            _singleByte = null;

            Advance();

            return CurrentByte;
        }
        byte PeekByte()
        {
            if (_singleByte != null)
                return _pipe.Buffer[0];

            if (_currentPosition + 1 < _length)
                return _pipe.Buffer[_currentPosition + 1];

            _singleByte = CurrentByte;

            Advance();

            return _pipe.Buffer[0];
        }
        void Advance()
        {
            if (_currentPosition < _length)
                _currentPosition++;
            else
            {
                FillBuffer();

                _currentPosition = 0;
                _length = _pipe.Operation.BytesTransferred;
            }
        }

        void FillBuffer()
        {
            _pipe.Stream.ReadAsync(_pipe.Buffer, 0, _pipe.Buffer.Length).WaitAndUnwarp();

            if (_pipe.Operation.LastError != SocketError.Success)
                throw new SocketException((int)_pipe.Operation.LastError);
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
                return;

            if (!int.TryParse(lengthStr, out var length))
                throw new FormatException();

            Advance();

            _session.RequestBody = ReadBody(length);
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
                if (!int.TryParse(lengthStr, out var length))
                    throw new FormatException();

                Advance();

                _session.ResponseBody = ReadBody(length);
            }

            if (_session.ResponseHeaders.TryGetValue("Transfer-Encoding", out var encoding) && encoding.OICContains("chunked"))
                _session.ResponseBody = ReadBodyChunks();
        }

        byte[] ReadBody(int length)
        {
            var buffer = new byte[length];
            var remaining = length;
            var offset = 0;

            while (remaining > 0)
            {
                var size = Math.Min(remaining, _length - _currentPosition);

                Buffer.BlockCopy(_pipe.Buffer, _currentPosition, buffer, offset, size);

                _currentPosition += size;
                remaining -= size;
                offset += size;

                if (remaining > 0 && _currentPosition == _length)
                {
                    FillBuffer();

                    _currentPosition = 0;
                    _length = _pipe.Operation.BytesTransferred;
                }
            }

            return buffer;
        }

        byte[] ReadBodyChunks()
        {
            var buffer = new MemoryStream();

            while (true)
            {
                var chunkSize = ReadHexadecimal();

                AssertNewline();

                if (chunkSize == 0)
                {
                    AssertNewline();

                    return buffer.ToArray();
                }

                Advance();

                var remaining = chunkSize;

                while (remaining > 0)
                {
                    var size = Math.Min(remaining, _length - _currentPosition);

                    buffer.Write(_pipe.Buffer, _currentPosition, size);

                    _currentPosition += size;
                    remaining -= size;

                    if (remaining > 0 && _currentPosition == _length)
                    {
                        FillBuffer();

                        _currentPosition = 0;
                        _length = _pipe.Operation.BytesTransferred;
                    }
                }

                if (CurrentByte != '\r')
                    throw new FormatException("Expect 0x0D");

                AssertChar('\n');
            }
        }

        HttpMethod ReadHttpMethod()
        {
            switch (ReadByte())
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
                    switch (ReadByte())
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
            var current = ReadByte();

            if (current == b)
                return;

            if (current - 0x20 == b)
                return;

            throw new FormatException("Expect " + (char)b);
        }

        void AssertChar(char c)
        {
            if (ReadByte() != c)
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
                var b = PeekByte();
                if (b == ' ')
                    return builder.ToString();

                builder.Append((char)b);

                Advance();
            }
        }
        string ReadUntilNewLine(StringBuilder builder)
        {
            builder.Clear();

            while (true)
            {
                var b = PeekByte();
                if (b == '\r')
                    return builder.ToString();

                builder.Append((char)b);

                Advance();
            }
        }

        HttpVersion ReadHttpVersion()
        {
            AssertChar('H');
            AssertChar('T');
            AssertChar('T');
            AssertChar('P');
            AssertChar('/');

            var major = ReadByte();
            AssertDigit(major);

            major -= (byte)'0';

            AssertChar('.');

            var minor = ReadByte();
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
                if (PeekByte() == '\r')
                    break;

                builder.Clear();

                while (true)
                {
                    var b = PeekByte();
                    if (!IsLetter(b) && b != '-')
                        break;

                    builder.Append((char)b);

                    Advance();
                }

                AssertChar(':');

                var key = string.Intern(builder.ToString());

                if (PeekByte() == ' ')
                {
                    Advance();
                }

                builder.Clear();

                while (true)
                {
                    var b = PeekByte();
                    if (b == '\r')
                        break;

                    builder.Append((char)b);

                    Advance();
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
                var b = PeekByte();
                if (!IsDigit(b))
                    return result;

                result = result * 10 + b - '0';

                Advance();
            }
        }

        int ReadHexadecimal()
        {
            var result = 0;

            while (true)
            {
                var b = PeekByte();
                if (!IsCharInHexadecimal(b))
                    return result;

                result = result * 16;

                if (IsDigit(b))
                    result += b - '0';
                else if (b >= 'a')
                    result += b - 'a' + 10;
                else
                    result += b - 'A' + 10;

                Advance();
            }
        }
        bool IsCharInHexadecimal(byte b) => b >= '0' && b <= '9' || (b >= 'A' && b <= 'F') || (b >= 'a' && b <= 'f');
    }
}
