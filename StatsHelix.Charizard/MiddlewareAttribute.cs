using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    /// <summary>
    /// The MiddlewareAttribute can be used to declare both application-wide and controller-level
    /// middleware handlers.
    /// Middleware handlers must take a HttpRequest and return Task&lt;HttpResponse&gt;
    ///
    /// Note that middleware can't be truly async - if it returns a non-null task, it commits
    /// to actually serving the request and can no longer decide to let the actual action handle it.
    ///
    /// Middleware runs for /all/ requests, regardless of whether there's an action for it.
    /// </summary>
    /// <remarks>
    /// When applied to a class, the class must be static.
    /// All functions inside that class with a suitable signature will be registered as
    /// application-level middleware.
    ///
    /// When applied to a method, the method may be static.
    /// The method must have a suitable signature and will be registered as controller-level
    /// middleware for the controller containing the method (if it is a controller) and all
    /// deriving controllers.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class MiddlewareAttribute : Attribute
    {
        /// <summary>
        /// If a controller has multiple middlewares on the same level, they are ordered by this value.
        /// </summary>
        /// <remarks>
        /// Note that the following order of execution is always maintained, irrespective of the Order property:
        /// 1. Application-level middleware.
        /// 2. Static controller-level middleware.
        /// 3. Instance controller-level middleware.
        /// </remarks>
        public int Order { get; set; }
    }
}
