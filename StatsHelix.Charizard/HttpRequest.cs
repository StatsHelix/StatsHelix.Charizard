using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace StatsHelix.Charizard
{
    public struct HttpRequest
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

        private readonly string PathUnderlying;
        private readonly int PathIndex;
        private readonly int PathLen;
        private readonly int QueryLen;

        public List<HttpHeader> Headers { get; internal set; }
        public string StringBody { get { return BodyEncoding.GetString(Body); } }
        public Encoding BodyEncoding { get; internal set; }
        public byte[] Body { get; internal set; }
        public HttpServer Server { get; internal set; }

        public HttpRequest(HttpMethod method, StringSegment path, List<HttpHeader> headers, Encoding bodyEncoding, HttpServer server)
        {
            Server = server;
            PathUnderlying = path.UnderlyingString;
            PathIndex = path.Index;
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
