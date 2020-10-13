using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Web;

namespace StatsHelix.Charizard
{
    public class HttpRequest
    {
        public StringSegment Path
        {
            get
            {
                return new StringSegment
                {
                    UnderlyingString = PathUnderlying,
                    Index = PathIndex,
                    Length = PathLen,
                };
            }
        }
        public StringSegment Querystring
        {
            get
            {
                return new StringSegment
                {
                    UnderlyingString = PathUnderlying,
                    Index = PathIndex + PathLen + 1,
                    Length = QueryLen,
                };
            }
        }

        public StringSegment PathAndQuery
        {
            get
            {
                if (QueryLen == 0)
                    return Path;

                return new StringSegment
                {
                    UnderlyingString = PathUnderlying,
                    Index = PathIndex,
                    Length = PathLen + QueryLen + 1,
                };
            }
        }
        public MethodInfo Target => Server.RoutingManager.Actions.GetValueOrDefault(Path.ToString(), null);


        public RangeHeaderValue Range
        {
            get
            {
                if (RangeHeaderValue.TryParse(GetHeader("range"), out var value))
                    return value;
                else
                    return null;
            }
        }


        private readonly string PathUnderlying;
        private readonly int PathIndex;
        private readonly int PathLen;
        private readonly int QueryLen;

        public HttpMethod Method { get; private set; }
        public List<HttpHeader> Headers { get; internal set; }
        public string StringBody { get { return BodyEncoding.GetString(Body); } }
        public Encoding BodyEncoding { get; internal set; }
        public byte[] Body { get; internal set; }
        public HttpServer Server { get; internal set; }

        public DateTime ReceivedAt { get; internal set; }
        public Stopwatch ReceiveTimer { get; internal set; }

        public IPEndPoint RemoteEndPoint { get; private set; }

        public bool IsWebSocket { get; }

        public HttpRequest(HttpMethod method, StringSegment path, List<HttpHeader> headers, Encoding bodyEncoding, DateTime receivedAt, Stopwatch receiveTimer, IPEndPoint remoteEndPoint, HttpServer server)
        {
            Method = method;
            Server = server;
            PathUnderlying = path.UnderlyingString;
            PathIndex = path.Index;
            ReceivedAt = receivedAt;
            ReceiveTimer = receiveTimer;
            RemoteEndPoint = remoteEndPoint;
            var qindex = path.IndexOf('?');
            if (qindex < 0)
            {
                PathLen = path.Length;
                QueryLen = 0;
            }
            else
            {
                PathLen = qindex;
                QueryLen = path.Length - qindex - 1;
            }

            Headers = headers;
            Body = null;
            BodyEncoding = bodyEncoding;

            IsWebSocket = false;
            if (method == HttpMethod.Get)
            {
                var connection = GetHeader("connection")?.ToLowerInvariant();
                if (connection != null)
                    IsWebSocket = HttpServer.HttpHeaderContains(connection, "upgrade") || HttpServer.HttpHeaderContains(connection, "websocket");
            }
        }

        public string GetHeader(string name)
        {
            foreach (var header in Headers)
                if (header.Name == name)
                    return header.Value;
            return null;
        }

        public string GetCookie(string name)
        {
            foreach (var header in Headers)
            {
                if (header.Name == "cookie")
                {
                    var cookies = header.Value;
                    for (int i = 0; i < cookies.Length; )
                    {
                        while (cookies[i] == ' ')
                            i++;

                        var nameEnd = cookies.IndexOf('=', i);
                        var valueEnd = cookies.IndexOf(';', nameEnd);
                        if (valueEnd < 0)
                            valueEnd = cookies.Length;

                        if (nameEnd < 0)
                            break;

                        if (cookies.Substring(i, nameEnd - i) == name)
                            return HttpUtility.UrlDecode(cookies.Substring(nameEnd + 1, valueEnd - nameEnd - 1));
                        i = valueEnd + 2;
                    }
                    break;
                }
            }

            return null;
        }
    }
}
