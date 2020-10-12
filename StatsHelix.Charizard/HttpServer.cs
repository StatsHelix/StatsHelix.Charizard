using ActuallyWorkingWebSockets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace StatsHelix.Charizard
{
    public class HttpServer
    {
        private const int SocketBacklog = 1024;
        private const string ServerHeader = "Server: StatsHelix Charizard v1.0";
        private const int MaxRequestBodySize = 1024 * 1024 * 1024;

        public IPEndPoint Endpoint { get; }

        /// <summary>
        /// This event is raised when an unexpected exception occurs.
        /// Unexpected exceptions are network failures or HTTP protocol
        /// violations - basically anything that instantly kills a client connection.
        /// </summary>
        public event Action<Exception> UnexpectedException;

        /// <summary>
        /// This event is raised when a bad request is received (and rejected with a 4xx error code).
        /// It can be used to log debug information.
        /// The string is the error reason, i.e. the message that we're returning to the client.
        /// </summary>
        public event Action<BadRequestEvent> BadRequest;

        /// <summary>
        /// Gets or sets the action exception handler.
        ///
        /// It handles exception thrown from controller actions.
        /// By default, it generates a 500 response.
        /// </summary>
        /// <value>The action exception handler.</value>
        public Func<Exception, HttpResponse> ActionExceptionHandler { get; set; } = DefaultActionExceptionHandler;

        /// <summary>
        /// Gets or sets the page-not-found handler.
        /// 
        /// This function gets called if a page can't be routed, and allows to customize
        /// handling this case.
        /// </summary>
        public Func<HttpRequest, Task<HttpResponse>> PageNotFoundHandler = (req) => Task.FromResult(HttpResponse.NotFound());

        /// <summary>
        /// An user-managed object to give context to the controllers.
        /// </summary>
        public object UserContext { get; set; }

        /// <summary>
        /// Settings for Newtonsoft.Json serialization and deserialization.
        /// </summary>
        public JsonSerializerSettings JsonSettings { get; set; }

        public static HttpResponse DefaultActionExceptionHandler(Exception e)
        {
#if DEBUG
            return HttpResponse.String("Internal server error: " + e, HttpStatus.InternalServerError);
#else
            return HttpResponse.String("Internal server error.", HttpStatus.InternalServerError);
#endif
        }

        internal readonly RoutingManager RoutingManager;

        public HttpServer(IPEndPoint endpoint, params Assembly[] controllerAssemblies)
        {
            Endpoint = endpoint;
            RoutingManager = new RoutingManager(this, controllerAssemblies);
        }

        public async Task Run()
        {
            var socket = new Socket(Endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(Endpoint);
            socket.Listen(SocketBacklog);

            while (true)
                HandleClient(await AcceptAsync(socket));
        }

        private static Task<Socket> AcceptAsync(Socket socket)
        {
            return Task.Factory.FromAsync(socket.BeginAccept, socket.EndAccept, null);
        }

        private static readonly string[] StatusStrings = new[]
        {
            "HTTP/1.1 200 OK",
            "HTTP/1.1 204 No Content",
            "HTTP/1.1 205 Reset Content",
            "HTTP/1.1 206 Partial Content",
            "HTTP/1.1 301 Moved Permanently",
            "HTTP/1.1 302 Found",
            "HTTP/1.1 303 See other",
            "HTTP/1.1 304 Not Modified",
            "HTTP/1.1 307 Temporary Redirect",
            "HTTP/1.1 400 Bad Request",
            "HTTP/1.1 403 Forbidden",
            "HTTP/1.1 404 Not Found",
            "HTTP/1.1 409 Conflict",
            "HTTP/1.1 411 Length Required",
            "HTTP/1.1 413 Request Entity Too Large",
            "HTTP/1.1 426 Upgrade Required",
            "HTTP/1.1 500 Internal Server Error",
        };
        private static readonly string[] ContentTypeStrings = new[]
        {
            "application/octet-stream",
            "application/json",
            "text/plain",
            "text/html",
        };

        private static Stream MakeSaneNetworkStream(Socket socket, out Stream readStream)
        {
            // This already flushes the underlying stream which is not what we want.
            // However, it doesn't matter anyways because flushing a NetworkStream is a NOP.
            // In fact, there is no way to control precise TCP packet behavior from C#.
            // The only solution that might work is to move buffering from kernel to userspace
            // by setting Socket.NoDelay and using a BufferedStream.
            //
            // The world is awful.
            socket.NoDelay = true;

            // It's important that we do NOT buffer reads though, they can (and should) go
            // straight through.
            readStream = new NetworkStream(socket, true);

            return new BufferedStream(readStream);
        }

        private async void HandleClient(Socket partner)
        {
            var receiveTimer = Stopwatch.StartNew();
            var receivedAt = DateTime.Now;

            try
            {
                Stream readStream;
                using (var writeStream = MakeSaneNetworkStream(partner, out readStream))
                using (var reader = new HttpRequestReaderStream(readStream))
                using (var writer = new StreamWriter(writeStream, new UTF8Encoding(false), 4096) { NewLine = "\r\n", AutoFlush = false })
                {
                    var headers = new List<HttpHeader>();
                    while (true)
                    {
                        var request = await ReadRequestHead(partner, receiveTimer, receivedAt, reader, writer, headers);
                        if (request == null)
                            return;

                        receiveTimer = null;
                        receivedAt = DateTime.MinValue;

                        var response = await RoutingManager.DispatchRequest(request);
                        using (response.ContentStream)
                        {
                            try
                            {
                                response.ResolveJsonContent(this);
                            }
                            catch (Exception e)
                            {
                                response = HttpResponse.String("Error serializing JSON: " + e, HttpStatus.InternalServerError);
                            }

                            if ((response.WebSocketHandler != null) && request.IsWebSocket)
                            {
                                await HandleWebSocket(partner, readStream, writer, response.WebSocketHandler, request);
                                return;
                            }

                            await WriteResponseHead(writer, response);

                            if (response.ContentStream != null)
                            {
                                await WriteChunkedResponse(writeStream, writer, response.ContentStream);
                            }
                            else
                            {
                                await WriteSimpleResponse(writeStream, writer, response);
                            }
                        }

                        // All is well - we can loop (keepalive).
                    }
                }
            }
            catch (Exception e)
            {
                UnexpectedException?.Invoke(e);
            }
        }

        private async Task<HttpRequest> WriteBadRequest(StringSegment rline, StreamWriter writer, string reason, HttpStatus status = HttpStatus.BadRequest)
        {
            // If we reach this, something is weird/wrong.
            // "Bye, have a great day!"
            await writer.WriteLineAsync(StatusStrings[(int)status]);
            await writer.WriteLineAsync(ServerHeader);
            await writer.WriteLineAsync("Connection: close");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(reason);
            await writer.FlushAsync();

            BadRequest?.Invoke(new BadRequestEvent(rline.ToString(), reason));

            return null; // only returning this so we can chain it to a return statement in the method below
        }

        private async Task<HttpRequest> ReadRequestHead(Socket partner, Stopwatch receiveTimer, DateTime receivedAt, HttpRequestReaderStream reader, StreamWriter writer, List<HttpHeader> headers)
        {
            var questing = new StringSegment(await reader.ReadLineAsync());
            if (questing.Empty)
                return null; // no request -> no response, close connection

            // If it is not the first request, we set the DateTime for this request here
            // right after receving the first line, which is presumably in the first packet.
            if (receivedAt == DateTime.MinValue)
            {
                receiveTimer = Stopwatch.StartNew();
                receivedAt = DateTime.Now;
            }

            headers.Clear();
            while (true)
            {
                var header = await reader.ReadLineAsync();
                if (String.IsNullOrEmpty(header))
                    break;

                var colon = header.IndexOf(':');
                var name = header.Substring(0, colon);
                var value = header.Substring(colon + 2); // skip colon + space

                headers.Add(new HttpHeader { Name = name.ToLowerInvariant(), Value = value });
            }

            var space1 = questing.IndexOf(' ');
            var space2 = questing.IndexOf(' ', space1 + 1); // if space1 is -1 this is fine as well

            if (!((space1 > 0) && (space2 > 0)))
                return await WriteBadRequest(questing, writer, "Invalid request line");
            var method = questing.Substring(0, space1);
            var path = questing.Substring(space1 + 1, space2 - space1 - 1);
            var version = questing.Substring(space2 + 1);
            if (!(version == "HTTP/1.1" && path[0] == '/'))
                return await WriteBadRequest(questing, writer, "Invalid protocol or path");
            path = path.Substring(1);

            HttpMethod prettyMethod;
            bool hasBody = false;
            if (method == "GET")
            {
                prettyMethod = HttpMethod.Get;
            }
            else if (method == "POST")
            {
                prettyMethod = HttpMethod.Post;
                hasBody = true;
            }
            else if (method == "PUT")
            {
                prettyMethod = HttpMethod.Put;
                hasBody = true;
            }
            else if (method == "DELETE")
            {
                prettyMethod = HttpMethod.Delete;
            }
            else
                return await WriteBadRequest(questing, writer, "Invalid or unsupported method");

            var request = new HttpRequest(prettyMethod, path, headers, Encoding.UTF8, receivedAt, receiveTimer, partner.RemoteEndPoint as IPEndPoint, this);
            if (hasBody)
            {
                int bodyLength;
                if (!int.TryParse(request.GetHeader("content-length"), out bodyLength))
                    return await WriteBadRequest(questing, writer, "Request has body but no content-length given!", HttpStatus.LengthRequired);

                if (bodyLength > MaxRequestBodySize)
                    return await WriteBadRequest(questing, writer, "Request body too large!", HttpStatus.EntityTooLarge);

                // read body into byte array (not sure about this tho)
                var body = new byte[bodyLength];
                int bodyRead = 0;
                while (bodyRead < body.Length)
                {
                    int read = await reader.ReadAsync(body, bodyRead, body.Length - bodyRead);
                    if (read == 0)
                    {
                        return await WriteBadRequest(questing, writer,
                            $"Invalid request: content length is {bodyLength} bytes, but stream closed after {bodyRead} bytes");
                    }
                    bodyRead += read;
                }

                request.Body = body;
            }
            return request;
        }

        private static async Task WriteResponseHead(StreamWriter writer, HttpResponse response)
        {
            await writer.WriteLineAsync(StatusStrings[(int)response.Status]);
            await writer.WriteLineAsync(ServerHeader);

            if (response.ContentType != ContentType.Custom)
            {
                await writer.WriteAsync("Content-Type: ");
                await writer.WriteLineAsync(ContentTypeStrings[(int)response.ContentType]);
            }

            if (response.ExtraHeaders != null)
            {
                foreach (var header in response.ExtraHeaders)
                {
                    await writer.WriteAsync(header.Name);
                    await writer.WriteAsync(": ");
                    await writer.WriteLineAsync(header.Value);
                }
            }
        }

        private static async Task WriteSimpleResponse(Stream writeStream, StreamWriter writer, HttpResponse response)
        {
            await writer.WriteAsync("Content-Length: ");
            await writer.WriteLineAsync(response.Content.Count.ToString());

            await writer.WriteLineAsync();
            // This flushes the BufferedStream as well which is NOT what we want.
            // Solving this would require us to either reimplement StreamWriter or
            // to wrap the BufferedStream in another Stream (because it's sealed).
            // Worth it? I don't know.
            await writer.FlushAsync();

            await writeStream.WriteAsync(response.Content.Array, response.Content.Offset, response.Content.Count);
            await writeStream.FlushAsync();
        }

        private static async Task WriteChunkedResponse(Stream writeStream, StreamWriter writer, QueueStream responseStream)
        {
            await writer.WriteLineAsync("Transfer-Encoding: chunked");
            await writer.WriteLineAsync();
            await writer.FlushAsync();


            var queue = responseStream.Queue;
            while (await queue.OutputAvailableAsync())
            {
                var chunk = await queue.ReceiveAsync();
                await writer.WriteLineAsync(chunk.Count.ToString("X"));

                await writer.FlushAsync();
                await writeStream.WriteAsync(chunk.Array, chunk.Offset, chunk.Count);

                await writer.WriteLineAsync();
            }

            // stream complete, write termination sequence:
            await writer.WriteLineAsync("0");
            // trailers would go here, if any
            await writer.WriteLineAsync();
            await writer.FlushAsync();
        }

        public async Task HandleWebSocket(Socket client, Stream netStream, StreamWriter writer, Func<WebSocketSession, Task> handler, HttpRequest request)
        {
            // calculate key
            // yes, the RFC defines a hardcoded value
            // and wow, this is surprisingly easy in C#
            var acceptKey = Convert.ToBase64String(System.Security.Cryptography.SHA1.Create().ComputeHash(
                Encoding.ASCII.GetBytes(request.GetHeader("sec-websocket-key") + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

            await writer.WriteLineAsync("HTTP/1.1 101 Switching Protocols");
            await writer.WriteLineAsync("Upgrade: websocket");
            await writer.WriteLineAsync("Connection: Upgrade");
            await writer.WriteLineAsync("Sec-WebSocket-Accept: " + acceptKey);
            await writer.WriteLineAsync();
            await writer.FlushAsync();

            // we no longer need the streamwriter, SWITCHING PROTOCOLS NOW
            // ---------------------------------------------------------------

            using (var session = new WebSocketSession(netStream, request.Path.ToString(), (IPEndPoint)client.RemoteEndPoint))
                await handler(session);
        }

        internal static bool HttpHeaderContains(string haystack, string needle)
        {
            int currentIndex = 0;
            while (true)
            {
                // the string terminates in a comma
                if (haystack.Length <= currentIndex)
                    return false;

                if (haystack[currentIndex] == ' ')
                {
                    currentIndex++;
                    continue;
                }

                if (currentIndex + needle.Length > haystack.Length)
                    return false;

                if (haystack.IndexOf(needle, currentIndex, needle.Length) != -1)
                {
                    // let's see if afterwards the string
                    // - terminates
                    // - or a comma
                    // - has whitespaces (in which case, ignore it, and continue)

                    for (int i = currentIndex + needle.Length; ; i++)
                    {

                        if (i >= haystack.Length)
                            return true;

                        if (haystack[i] == ',')
                            return true;

                        if (haystack[i] == ' ')
                            continue;

                        return false;
                    }
                }

                currentIndex = haystack.IndexOf(',', currentIndex + 1) + 1;
                if (currentIndex == 0)
                    return false;
            }
        }
    }
}
