using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Sakuno.Nekomimi.IO;

namespace Sakuno.Nekomimi
{
    public class Session
    {
        internal Pipe ServerPipe { get; }

        public HttpMethod Method { get; internal set; }

        internal HttpVersion HttpVersion { get; set; }

        public string Host { get; internal set; }

        public int Port { get; internal set; }

        public string FullUri { get; internal set; }

        public string LocalPath { get; internal set; }

        public bool IsHTTPS { get; internal set; }

        public bool Decompress { get; set; }

        internal SessionStatus Status { get; set; }
        internal void VerifyStatus(SessionStatus status)
        {
            if (Status != status)
                throw new InvalidOperationException($"Bad session status. Expected: {status}, Current: {Status}");
        }
        internal void VerifyStatusAfter(SessionStatus status)
        {
            if (Status < status)
                throw new InvalidOperationException($"Bad session status. Expected: {status}, Current: {Status}");
        }

        public IDictionary<string, string> RequestHeaders { get; internal set; }

        internal SegmentBuffer RequestBodyBuffer;
        private string _requestBodyString;

        public Stream GetRequestBodyStream()
        {
            VerifyStatusAfter(SessionStatus.BeforeRequest);
            return RequestBodyBuffer.CreateStream();
        }

        public string GetRequestBodyAsString()
        {
            VerifyStatusAfter(SessionStatus.BeforeRequest);
            if (_requestBodyString == null)
                using (var reader = new StreamReader(GetRequestBodyStream()))
                    _requestBodyString = reader.ReadToEnd();
            return _requestBodyString;
        }

        public byte[] GetRequestBody()
        {
            VerifyStatusAfter(SessionStatus.BeforeRequest);
            return RequestBodyBuffer.ReadToEnd();
        }

        public IPEndPoint ForwardDestination { get; internal set; }

        internal Pipe ClientPipe { get; set; }

        public int StatusCode { get; internal set; }

        internal string ReasonPhase { get; set; }

        public IDictionary<string, string> ResponseHeaders { get; internal set; }

        internal SegmentBuffer ResponseBodyBuffer;
        private string _responseBodyString;
        private SegmentBuffer ResponseDecompressionBuffer;

        private SegmentBuffer CheckResponseDecompression()
        {
            if (!Decompress) return ResponseBodyBuffer;
            if (ResponseDecompressionBuffer == null)
            {
                Stream decompressionStream = null;
                if (ResponseHeaders.TryGetValue("Content-Encoding", out var encoding))
                {
                    if (encoding.OICEquals("gzip"))
                        decompressionStream = new GZipStream(ResponseBodyBuffer.CreateStream(), CompressionMode.Decompress);
                    else if (encoding.OICEquals("deflate"))
                        decompressionStream = new DeflateStream(ResponseBodyBuffer.CreateStream(), CompressionMode.Decompress);
                }
                if (decompressionStream != null)
                    ResponseDecompressionBuffer = new SegmentBuffer(decompressionStream);
                else
                    ResponseDecompressionBuffer = ResponseBodyBuffer;
            }
            return ResponseDecompressionBuffer;
        }

        public Stream GetResponseBodyStream()
        {
            VerifyStatusAfter(SessionStatus.BeforeResponse);
            return CheckResponseDecompression().CreateStream();
        }

        public string GetResponseBodyAsString()
        {
            VerifyStatusAfter(SessionStatus.BeforeResponse);
            if (_responseBodyString == null)
                using (var reader = new StreamReader(GetResponseBodyStream()))
                    _responseBodyString = reader.ReadToEnd();
            return _requestBodyString;
        }

        public byte[] GetResponseBody()
        {
            VerifyStatusAfter(SessionStatus.BeforeResponse);
            return CheckResponseDecompression().ReadToEnd();
        }

        public Session(Socket clientSocket)
        {
            ServerPipe = new Pipe(clientSocket);
        }

        internal async Task CreateClientPipeAndConnect(IPEndPoint remoteEndPoint)
        {
            var socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            var clientStream = new SocketStream(socket);
            await clientStream.ConnectAsync(remoteEndPoint);
            ClientPipe = new Pipe(socket);
        }
    }
}
