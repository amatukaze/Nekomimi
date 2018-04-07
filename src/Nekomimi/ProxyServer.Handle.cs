using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;
using System.Linq;
using System.Net.Http;
using System.Text.Formatting;
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
            var outputText = connection.Output.AsTextOutput(SymbolTable.InvariantUtf8);
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
                    ReadOnlySequence<byte> buffer = result.Buffer;
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
                                outputText.Format("HTTP/{0}.{1} 502 Bad Gateway\r\n\r\n",
                                    session.Request.Version.Major,
                                    session.Request.Version.Minor);
                                await outputText.FlushAsync();
                            }
                            else
                            {
                                outputText.Format("HTTP/{0}.{1} 200 Connection Established\r\n\r\n",
                                    session.Request.Version.Major,
                                    session.Request.Version.Minor);
                                await outputText.FlushAsync();
                                EatException(SslConnecting, session);
                                Task upward = connection.Input.CopyToAsync(upstream.Output),
                                    downward = upstream.Input.CopyToAsync(connection.Output);
                                await Task.WhenAll(upward, downward);
                            }
                            break;
                        }

                    if (session.Request.Headers.ExpectContinue == true)
                    {
                        outputText.Append("100 Continue\r\n");
                        await connection.Output.FlushAsync();
                    }

                    if (builder.PendingHeaders.TryGetValue("Content-Length", out var lengthStr)
                        && int.TryParse(lengthStr, out var length))
                    {
                        var requestBody = new byte[length];
                        ArraySegment<byte> remained = new ArraySegment<byte>(requestBody);
                        while (remained.Count > 0)
                        {
                            int bytesRead = await connection.Input.ReadAsync(remained);
                            if (bytesRead == 0)
                                throw new FormatException("Incomplete request content");
                            remained = new ArraySegment<byte>(requestBody, remained.Offset + bytesRead, remained.Count - bytesRead);
                        }

                        session.Request.Content = new ByteArrayContent(requestBody);
                        foreach (var header in builder.PendingHeaders)
                            session.Request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    EatException(BeforeRequest, session);

                    HttpResponseMessage response;
                    try
                    {
                        response = session.Response ?? await _httpClient.SendAsync(session.Request);
                        session.RequestSent = true;
                    }
                    catch (Exception ex)
                    {
                        outputText.Format("HTTP/{0}.{1} 502 Bad Gateway\r\n\r\n",
                            session.Request.Version.Major,
                            session.Request.Version.Minor);
                        await outputText.FlushAsync();
                        SessionFailed?.Invoke(session, ex);
                        continue;
                    }

                    EatException(AfterRequest, session);
                    session.Response = response;
                    EatException(BeforeResponse, session);

                    outputText.Format("HTTP/{0}.{1} {2} {3}\r\n",
                        response.Version.Major,
                        response.Version.Minor,
                        (int)response.StatusCode,
                        response.ReasonPhrase);

                    if (response.Content.Headers.ContentLength == null)
                        response.Headers.TransferEncodingChunked = true;
                    else
                        response.Headers.TransferEncodingChunked = false;

                    foreach (var header in response.Headers.Concat(response.Content.Headers))
                        outputText.Format("{0}: {1}\r\n",
                            header.Key,
                            string.Join("; ", header.Value));

                    outputText.Append("\r\n");

                    if (response.Content != null)
                    {
                        var contentStream = await response.Content.ReadAsStreamAsync();
                        if (response.Content.Headers.ContentLength != null)
                            await contentStream.CopyToAsync(connection.GetStream());
                        else
                        {
                            var sendBuffer = ArrayPool<byte>.Shared.Rent(4096);
                            try
                            {
                                int bytesRead;
                                do
                                {
                                    bytesRead = await contentStream.ReadAsync(sendBuffer, 0, sendBuffer.Length);
                                    outputText.Append(bytesRead, 'x');
                                    outputText.Append("\r\n");
                                    await connection.Output.WriteAsync(new ReadOnlyMemory<byte>(sendBuffer, 0, bytesRead));
                                    outputText.Append("\r\n");
                                }
                                while (bytesRead > 0);
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(sendBuffer);
                            }
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
    }
}
