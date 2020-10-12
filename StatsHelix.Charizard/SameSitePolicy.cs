using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    public enum SameSitePolicy
    {
        /// <summary>
        /// Cookies will only be sent in a first-party context and not be sent along with requests initiated by third party websites.
        /// </summary>
        Strict,

        /// <summary>
        /// Cookies are allowed to be sent with top-level navigations and will be sent along with GET request initiated by third party website. This is the default value in modern browsers.
        /// </summary>
        Lax,

        /// <summary>
        /// Cookies will be sent in all contexts, i.e sending cross-origin is allowed.
        /// None requires the Secure attribute in latest browser versions.
        /// </summary>
        None,
    }
}
