using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// The headers an allowed cross-origin response carries, written the same way by the middleware and
/// by a route's own filter.
/// </summary>
internal static class CorsHeaders
{
    /// <summary>
    /// What an allowed cross-origin response carries. Not the preflight set: <c>Allow-Methods</c>,
    /// <c>Allow-Headers</c> and <c>Max-Age</c> mean nothing here and were only ever noise.
    /// </summary>
    public static void WriteActual(
        IDictionary<string, StringValues> headers,
        CorsConfiguration policy,
        string origin
    )
    {
        headers[KnownHeaders.Cors.AccessControlAllowOrigin] = AllowOrigin(policy, origin);

        if (policy.AllowCredentials && !policy.AllowAnyOrigin)
        {
            headers[KnownHeaders.Cors.AccessControlAllowCredentials] = "true";
        }

        if (policy.ExposedHeaders.Count > 0)
        {
            headers[KnownHeaders.Cors.AccessControlExposeHeaders] = string.Join(
                ", ",
                policy.ExposedHeaders
            );
        }
    }

    /// <summary>
    /// The origin as written, or <c>*</c> when any is allowed and no credentials are in play.
    /// </summary>
    /// <remarks>
    /// Echoing the caller's origin even under <c>AllowAnyOrigin</c> would be more precise, but
    /// <c>*</c> is cacheable across origins and that is the whole reason to have configured it.
    /// </remarks>
    public static StringValues AllowOrigin(CorsConfiguration policy, string origin) =>
        policy.AllowAnyOrigin && !policy.AllowCredentials ? "*" : origin;
}
