using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    // Keep this in sync with the string[] in HttpServer or things may get hilarious.
    public enum ContentType : byte
    {
        OctetStream,
        Json,
        Plaintext,
        Html,
        // (add new types here - remember the warning above!)

        /// <summary>
        /// When this enum doesn't have the type you need, use this one.
        /// It prevents Charizard from emitting a Content-Type header, allowing you
        /// to set a custom value via ExtraHeaders/SetHeader().
        /// </summary>
        Custom,
    }
}
