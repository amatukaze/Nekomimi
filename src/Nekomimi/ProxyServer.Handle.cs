using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Text.Http.Parser;
using System.Text.Utf8;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    public partial class ProxyServer
    {
        private struct RequestBuilder : IHttpRequestLineHandler, IHttpHeadersHandler
        {
            public Session Session;
            public Dictionary<string, string> PendingHeaders;
            public void Reset(Session session)
            {
                this.Session = session;
                PendingHeaders.Clear();
            }

            public void OnStartLine(Http.Method method, Http.Version version, ReadOnlySpan<byte> target, ReadOnlySpan<byte> path, ReadOnlySpan<byte> query, ReadOnlySpan<byte> customMethod, bool pathEncoded)
            {
                Session.Request = new HttpRequestMessage
                {
                    Method = HttpConstants.MapMethod(method) ?? new HttpMethod(new Utf8String(customMethod).ToString()),
                    Version = HttpConstants.MapVersion(version),
                };

                if (method == Http.Method.Connect)
                {
                    Session.IsHTTPS = true;
                    Session.Request.RequestUri = new Uri("https://" + new Utf8String(target));
                }
                else
                    Session.Request.RequestUri = new Uri(new Utf8String(target).ToString());
            }

            public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
            {
                string nameStr = new Utf8String(name).ToString(), valueStr = new Utf8String(value).ToString();
                if (!Session.Request.Headers.TryAddWithoutValidation(nameStr, valueStr))
                    if (PendingHeaders.TryGetValue(nameStr, out string existed))
                        PendingHeaders[nameStr] = existed + "; " + valueStr;
                    else
                        PendingHeaders.Add(nameStr, valueStr);
            }
        }

        private static async ValueTask<ReadOnlySequence<byte>> MoreAsync(PipeReader reader, SequencePosition consumed, SequencePosition examined)
        {
            reader.AdvanceTo(consumed, examined);
            var result = await reader.ReadAsync();
            if (result.IsCompleted && result.Buffer.IsEmpty)
                throw new FormatException("Request incomplete.");
            return result.Buffer;
        }

        private async Task HandleConnection(IDuplexPipe connection)
        {
            var parser = new HttpParser(true);
            var output = connection.Output;
            RequestBuilder builder = default;
            builder.PendingHeaders = new Dictionary<string, string>(10);

            for (var result = await connection.Input.ReadAsync();
                !(result.IsCompleted && result.Buffer.IsEmpty);
                result = await connection.Input.ReadAsync())
            {
                var session = new Session();
                try
                {
                    builder.Reset(session);
                    var buffer = result.Buffer;
                    SequencePosition consumed = buffer.Start, examined = buffer.Start;

                    do buffer = await MoreAsync(connection.Input, consumed, examined);
                    while (!parser.ParseRequestLine(builder, in buffer, out consumed, out examined));

                    do buffer = await MoreAsync(connection.Input, consumed, examined);
                    while (!parser.ParseHeaders(builder, in buffer, out consumed, out examined, out _));

                    connection.Input.AdvanceTo(consumed, examined);

                    if (session.IsHTTPS)
                        using (var upstream = await BuildSSLAsync(builder))
                        {
                            if (upstream == null)
                            {
                                WriteUtf8(output,
                                    $"HTTP/{session.Request.Version.Major}.{session.Request.Version.Minor} 502 Bad Gateway\r\n\r\n");
                                await output.FlushAsync();
                            }
                            else
                            {
                                WriteUtf8(output,
                                    $"HTTP/{session.Request.Version.Major}.{session.Request.Version.Minor} 200 Connection Established\r\n\r\n");
                                await output.FlushAsync();
                                EatException(SslConnecting, session);
                                Task upward = CopyAsync(connection.Input, upstream.Output),
                                    downward = CopyAsync(upstream.Input, connection.Output);
                                await Task.WhenAll(upward, downward);
                            }
                            break;
                        }

                    if (session.Request.Headers.ExpectContinue == true)
                    {
                        WriteUtf8(output, "100 Continue\r\n");
                        await connection.Output.FlushAsync();
                    }

                    if (builder.PendingHeaders.TryGetValue("Content-Length", out var lengthStr)
                        && int.TryParse(lengthStr, out var length))
                    {
                        var requestBody = new byte[length];
                        var remained = requestBody.AsMemory();
                        while (remained.Length > 0)
                        {
                            var r = await connection.Input.ReadAsync();
                            var b = r.Buffer;
                            if (r.IsCompleted && b.IsEmpty)
                                throw new FormatException("Incomplete request content");
                            if (b.Length > remained.Length)
                                b = b.Slice(0, remained.Length);
                            foreach (var sec in b)
                            {
                                sec.CopyTo(remained);
                                remained = remained.Slice(sec.Length);
                            }
                            connection.Input.AdvanceTo(b.End);
                        }

                        session.Request.Content = new ByteArrayContent(requestBody);
                        foreach (var header in builder.PendingHeaders)
                            session.Request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    EatException(BeforeRequest, session);

                    HttpResponseMessage response = null;
                    try
                    {
                        while (true)
                            try
                            {
                                response = session.Response ?? await _httpClient.SendAsync(session.Request);
                                break;
                            }
                            catch (HttpRequestException innerException)
                            {
                                var e = new RequestFailedEventArgs(innerException);
                                EatException(RequestFailed, session, e);
                                if (session.Response != null)
                                    break;
                                if (e.RetryDueTime is TimeSpan dueTime)
                                {
                                    await Task.Delay(dueTime);
                                    continue;
                                }
                                throw;
                            }
                    }
                    catch (Exception ex)
                    {
                        WriteUtf8(output,
                            $"HTTP/{session.Request.Version.Major}.{session.Request.Version.Minor} 502 Bad Gateway\r\n\r\n");
                        await output.FlushAsync();
                        SessionFailed?.Invoke(session, ex);
                        break;
                    }

                    session.RequestSent = true;
                    EatException(AfterRequest, session);
                    session.Response = response;
                    EatException(BeforeResponse, session);

                    WriteUtf8(output,
                        $"HTTP/{response.Version.Major}.{response.Version.Minor} {(int)response.StatusCode} {response.ReasonPhrase}\r\n");

                    if (response.Content.Headers.ContentLength == null)
                        response.Headers.TransferEncodingChunked = true;
                    else
                        response.Headers.TransferEncodingChunked = false;

                    foreach (var header in response.Headers.Concat(response.Content.Headers))
                        WriteUtf8(output,
                            $"{header.Key}: {string.Join("; ", header.Value)}\r\n");

                    WriteUtf8(output, "\r\n");

                    if (response.Content != null)
                    {
                        var contentStream = await response.Content.ReadAsStreamAsync();
                        var sendBuffer = ArrayPool<byte>.Shared.Rent(81920);
                        try
                        {
                            int bytesRead;
                            if (response.Content.Headers.ContentLength != null)
                                do
                                {
                                    bytesRead = await contentStream.ReadAsync(sendBuffer, 0, sendBuffer.Length);
                                    var memory = new ReadOnlyMemory<byte>(sendBuffer, 0, bytesRead);
                                    EatException(DataReceiving, session, memory);
                                    await connection.Output.WriteAsync(memory);
                                }
                                while (bytesRead > 0);
                            else
                                do
                                {
                                    bytesRead = await contentStream.ReadAsync(sendBuffer, 0, sendBuffer.Length);
                                    var memory = new ReadOnlyMemory<byte>(sendBuffer, 0, bytesRead);
                                    EatException(DataReceiving, session, memory);
                                    WriteUtf8(output, bytesRead.ToString("x"));
                                    WriteUtf8(output, "\r\n");
                                    await connection.Output.WriteAsync(memory);
                                    WriteUtf8(output, "\r\n");
                                }
                                while (bytesRead > 0);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(sendBuffer);
                        }
                        contentStream.Seek(0, System.IO.SeekOrigin.Begin);
                    }

                    await connection.Output.FlushAsync();
                    session.ResponseSent = true;
                    EatException(AfterResponse, session);
                }
                catch (Exception ex)
                {
                    SessionFailed?.Invoke(session, ex);
                    break;
                }
                finally
                {
                    parser.Reset();
                }
            }
        }

        private static async Task CopyAsync(PipeReader reader, PipeWriter writer)
        {
            while (true)
            {
                var result = await reader.ReadAsync().ConfigureAwait(false);
                var buffer = result.Buffer;
                try
                {
                    if (result.IsCompleted && buffer.IsEmpty)
                        return;
                    foreach (var m in buffer)
                        writer.Write(m.Span);
                    await writer.FlushAsync().ConfigureAwait(false);
                }
                finally
                {
                    reader.AdvanceTo(buffer.End);
                }
            }
        }

        private static void WriteUtf8(PipeWriter writer, string @string)
            => writer.Write(System.Text.Encoding.UTF8.GetBytes(@string));
    }
}
