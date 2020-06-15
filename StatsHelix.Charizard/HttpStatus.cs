using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    // Keep this in sync with the string[] in HttpServer or things may get hilarious.
    public enum HttpStatus : byte
    {
        // 2XX
        Ok,
        NoContent,
        ResetContent,
        PartialContent,

        // 3XX
        MovedPermanently,
        Found,
        SeeOther,
        NotModified,

        // 4XX
        BadRequest,
        Forbidden,
        NotFound,
        Conflict,
        LengthRequired,
        EntityTooLarge,
        UpgradeRequired,

        // 5XX
        InternalServerError,
    }
}
