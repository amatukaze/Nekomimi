﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Sakuno.Nekomimi.IO;

namespace Sakuno.Nekomimi
{
    public class Session
    {
        internal Pipe ServerPipe { get; }

        internal HttpMethod _method;
        public HttpMethod Method
        {
            get => _method;
            set
            {
                if (_method != value)
                    _method = value;
            }
        }

        internal HttpVersion HttpVersion { get; set; }

        internal string _host;
        public string Host
        {
            get => _host;
            set
            {
                if (_host != value)
                    _host = value;
            }
        }

        internal int _port = 80;
        public int Port
        {
            get => _port;
            set
            {
                if (_port != value)
                {
                    _port = value;
                }
            }
        }

        internal string _path;
        public string Path
        {
            get => _path;
            set
            {
                if (_path != value)
                    _path = value;
            }
        }

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

        IPEndPoint _forwardDestination;
        public IPEndPoint ForwardDestination
        {
            get => _forwardDestination;
            set
            {
                if (_forwardDestination != value)
                    _forwardDestination = value;
            }
        }

        internal Pipe ClientPipe { get; set; }

        internal int _statusCode;
        public int StatusCode
        {
            get => _statusCode;
            set
            {
                if (_statusCode != value)
                    _statusCode = value;
            }
        }

        internal string ReasonPhase { get; set; }

        public IDictionary<string, string> ResponseHeaders { get; internal set; }

        internal SegmentBuffer ResponseBodyBuffer;
        private string _responseBodyString;

        public Stream GetResponseBodyStream()
        {
            VerifyStatusAfter(SessionStatus.BeforeResponse);
            return ResponseBodyBuffer.CreateStream();
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
            return ResponseBodyBuffer.ReadToEnd();
        }

        public Session(Socket clientSocket)
        {
            ServerPipe = new Pipe((new SocketStream(clientSocket)));
        }

        internal async Task CreateClientPipeAndConnect(IPEndPoint remoteEndPoint)
        {
            var socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            var clientStream = new SocketStream(socket);
            await clientStream.ConnectAsync(remoteEndPoint);
            ClientPipe = new Pipe(new SocketStream(socket));
        }
    }
}
