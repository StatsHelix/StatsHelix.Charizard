using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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


        public class RangeHeader
        {
            /// <summary>
            /// The unit of the range numbers.
            /// Should always be 'bytes', unless your use case is super esoteric.
            /// (theoretically, you should not even have to verify this unless you want to be pedantic)
            ///
            /// By the way, you should send an Accept-Ranges header with the units that you support.
            /// </summary>
            public string Unit { get; set; }

            /// <summary>
            /// The actual header ranges.
            ///
            /// If this contains a single element, you can just reply with a regular Partial Content response.
            /// If this contains multiple ranges, you have to do multipart shenanigans (almost always unnecessary, don't bother supporting this).
            ///
            /// You can rely on the fact that this will never be empty.
            /// </summary>
            public IEnumerable<Range> Ranges { get; set; }

            /// <summary>
            /// A range is described by a start and an end location.
            ///
            /// In order to be valid, a range must have either a start or an end value.
            /// (otherwise this would select everything and then you just have a regular non-range request)
            ///
            /// If the end value is missing, it means they want everything from specified start up to the end of the entity.
            /// If the start value is missing, it means you should go to the end of the entity,
            /// then go backwards by End, and then start from there all the way to the end.
            /// </summary>
            public struct Range
            {
                public long? Start { get; set; }
                public long? End { get; set; }

                public bool Valid => Start.HasValue || End.HasValue;

                public Range(string start, string end)
                {
                    Start = string.IsNullOrEmpty(start) ? null : new long?(long.Parse(start));
                    End = string.IsNullOrEmpty(end) ? null : new long?(long.Parse(end));
                }

                public override string ToString() => $"{Start}-{End}";
            }

            public override string ToString() => $"{Unit}={String.Join(",", Ranges)}";
        }

        private static readonly Regex RangeHeaderPattern = new Regex(@"^(?<unit>\w+)=(?<start>\d*)-(?<end>\d*)(\s*,\s*(?<start>\d*)-(?<end>\d*))*$", RegexOptions.Compiled);
        public RangeHeader Range
        {
            get
            {
                var range = GetHeader("range");
                if (range != null)
                {
                    var match = RangeHeaderPattern.Match(range);
                    if (match.Success)
                    {
                        var ranges = match.Groups["start"].Captures.ToEnumerable().Zip(match.Groups["end"].Captures.ToEnumerable(), (start, end) => new RangeHeader.Range(start, end)).ToArray();
                        if (ranges.All(x => x.Valid))
                        {
                            return new RangeHeader
                            {
                                Unit = match.Groups["unit"].Value,
                                Ranges = ranges,
                            };
                        }
                        // TODO: technically, we could be pedantic and somehow cause a 400 Bad Request here
                        //       (impossible from a property - I KNOW - but obviously the interface would just be different then)
                        // on the other hand, it is often nicer to just do the thing that works instead (ignore the range header)
                        // depending on how you read the MDN docs, this is actually the required behavior by the spec as well
                    }
                }

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
