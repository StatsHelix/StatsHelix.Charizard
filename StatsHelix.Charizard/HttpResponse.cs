using ActuallyWorkingWebSockets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace StatsHelix.Charizard
{
    public struct HttpResponse
    {
        /// <summary>
        /// Disables setting security attributes for all cookies.
        /// Needless to say, THIS IS INSECURE!
        /// (but useful/vital for debugging)
        /// </summary>
        public static bool InsecureMode_DoNotUseThisInProduction { get; set; }

        public ArraySegment<byte> Content { get; set; }

        public List<HttpHeader> ExtraHeaders { get; private set; }
        public HttpStatus Status { get; set; }
        public ContentType ContentType { get; set; }

        internal Func<WebSocketSession, Task> WebSocketHandler { get; set; }

        // Accidentally quadratic, I know.
        // But n is small and I like the ergonomics of doing it this way.
        public HttpResponse SetHeader(string name, string value)
        {
            int? found = null;
            if (ExtraHeaders == null)
            {
                ExtraHeaders = new List<HttpHeader>();
            }
            else
            {
                for (int i = 0; i < ExtraHeaders.Count; i++)
                {
                    if (ExtraHeaders[i].Name == name)
                    {
                        found = i;
                        break;
                    }
                }
            }

            var header = new HttpHeader { Name = name, Value = value };
            if (found.HasValue)
                ExtraHeaders[found.Value] = header;
            else
                ExtraHeaders.Add(header);

            return this;
        }

        /// <summary>
        /// Adds a header. Note that this will not override the header if it was already set - both
        /// instances will be sent. Therefore, you should use this only for headers that may be
        /// duplicated (according to HTTP) and SetHeader() for everything else.
        /// </summary>
        /// <param name="name">The header name.</param>
        /// <param name="value">The header value.</param>
        /// <returns>The modified HttpResponse.</returns>
        public HttpResponse AddHeader(string name, string value)
        {
            if (ExtraHeaders == null)
                ExtraHeaders = new List<HttpHeader>();

            ExtraHeaders.Add(new HttpHeader { Name = name, Value = value });
            return this;
        }

        /// <summary>
        /// Sets a cookie.
        /// For simplicity, this will (intentionally) not clean up previous Set-Cookie headers for
        /// the same cookie.
        /// Reasons:
        /// * I'm lazy.
        /// * Shouldn't matter anyways (hopefully).
        /// </summary>
        /// <param name="name">The cookie name.</param>
        /// <param name="value">The cookie value.</param>
        /// <param name="expiration">The cookie's expiration date.</param>
        /// <returns></returns>
        public HttpResponse SetCookie(string name, string value, bool secure, DateTimeOffset? expiration = null, string path = "/")
        {
            var header = name + "=" + HttpUtility.UrlEncode(value) + "; Path=" + path;
            if (expiration != null)
                header += "; Expires=" + expiration.Value.ToUniversalTime().ToString("r");

            if (secure && !InsecureMode_DoNotUseThisInProduction)
                header += "; HttpOnly; Secure";

            return AddHeader("Set-Cookie", header);
        }

        private static readonly UTF8Encoding UTF8 = new UTF8Encoding(false);

        private static readonly byte[] Default404Message = UTF8.GetBytes("Not found. :(");
        private static readonly byte[] DefaultRedirectMessage = UTF8.GetBytes("Redirect.");

        public static HttpResponse NotFound()
        {
            return Data(Default404Message, HttpStatus.NotFound, ContentType.Plaintext);
        }

        public static HttpResponse Json(object o, HttpStatus status = HttpStatus.Ok, ContentType contentType = ContentType.Json)
        {
            return String(JsonConvert.SerializeObject(o), status, contentType);
        }

        public static HttpResponse String(string s, HttpStatus status = HttpStatus.Ok, ContentType contentType = ContentType.Plaintext)
        {
            return Data(UTF8.GetBytes(s), status, contentType);
        }

        public static HttpResponse Data(byte[] data, HttpStatus status = HttpStatus.Ok, ContentType contentType = ContentType.OctetStream)
        {
            return Data(new ArraySegment<byte>(data), status, contentType);
        }

        public static HttpResponse Data(ArraySegment<byte> data, HttpStatus status = HttpStatus.Ok, ContentType contentType = ContentType.OctetStream)
        {
            if ((data.Count == 0) && (status == HttpStatus.Ok))
                status = HttpStatus.NoContent;

            return new HttpResponse
            {
                Status = status,
                ContentType = contentType,
                Content = data,
            };
        }

        public static HttpResponse Redirect(string target, bool permanent = false)
        {
            return new HttpResponse
            {
                Status = permanent ? HttpStatus.MovedPermanently : HttpStatus.Found,
                ContentType = ContentType.Plaintext,
                Content = new ArraySegment<byte>(DefaultRedirectMessage),
                ExtraHeaders = new List<HttpHeader>(1) { new HttpHeader { Name = "Location", Value = target } },
            };
        }

        public static HttpResponse OpenWebSocket(Func<WebSocketSession, Task> handler)
        {
            return new HttpResponse
            {
                WebSocketHandler = handler,
            };
        }
    }
}
