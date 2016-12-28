using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    [Serializable]
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class ControllerAttribute : Attribute
    {
        /// <summary>
        /// The prefix this controller is mapped to.
        /// Defaults to $"{ClassNameWithoutController}/".
        /// Note that this must not include the leading slash.
        /// </summary>
        public string Prefix { get; set; } = null;

        /// <summary>
        /// The regex that parses additional querystring parameters
        /// from the URL path.
        /// Defaults to null which means that no such regex will be used.
        /// Note that regexes are fast but still orders of magnitude slower
        /// than not using regexes. ;)
        /// </summary>
        public string PathParamsPattern { get; set; } = null;
    }
}
