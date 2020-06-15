using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    public struct BadRequestEvent
    {
        /// <summary>
        /// The first line of the HTTP request.
        /// Unfortunately we can't give you more than this, since the request is bad.
        /// </summary>
        public string RequestLine { get; }

        /// <summary>
        /// The reason why we're rejecting this request.
        /// </summary>
        public string Reason { get; }

        public BadRequestEvent(string rline, string reason)
        {
            RequestLine = rline;
            Reason = reason;
        }
    }
}
