namespace Hardened.Requests.Abstract.Headers;

public static class KnownHeaders {
    public const string Accept = "Accept";

    /// <summary>
    /// The verbs a resource does answer. Required on a 405 - it is what makes the response
    /// actionable rather than merely correct.
    /// </summary>
    public const string Allow = "Allow";

    public const string AcceptEncoding = "Accept-Encoding";

    public const string CacheControl = "Cache-Control";

    public const string ContentEncoding = "Content-Encoding";

    public const string ContentType = "Content-Type";

    public const string ContentLength = "Content-Length";

    public const string Cookie = "Cookie";

    /// <summary>Where a redirect points. Required on a 3xx that has one.</summary>
    public const string Location = "Location";

    public const string IfMatch = "If-Match";

    public const string IfNoneMatch = "If-None-Match";

    public const string Origin = "Origin";

    /// <summary>
    /// Which request headers this response's content depends on.
    /// </summary>
    /// <remarks>
    /// Required whenever a response is built from a request header, which for CORS means every
    /// response carrying an <c>Access-Control-Allow-Origin</c> derived from the request's
    /// <c>Origin</c>. Without it a shared cache is entitled to hand one origin's response - allow
    /// header included - to another.
    /// </remarks>
    public const string Vary = "Vary";

    /// <summary>How long to wait before trying again. Belongs on a 429 and on a 503.</summary>
    public const string RetryAfter = "Retry-After";

    public static class Cors {
        public const string AccessControlAllowOrigin = "Access-Control-Allow-Origin";

        public const string AccessControlAllowHeaders = "Access-Control-Allow-Headers";

        public const string AccessControlAllowMethods = "Access-Control-Allow-Methods";

        public const string AccessControlMaxAge = "Access-Control-Max-Age";

        /// <summary>
        /// That the browser may send credentials. Never valid alongside a <c>*</c> origin, and
        /// browsers reject the pair.
        /// </summary>
        public const string AccessControlAllowCredentials = "Access-Control-Allow-Credentials";

        /// <summary>
        /// Which response headers scripts may read. Without it a cross-origin caller can read only
        /// the CORS-safelisted set, whatever else the response carries.
        /// </summary>
        public const string AccessControlExposeHeaders = "Access-Control-Expose-Headers";

        /// <summary>The verb the preflight is asking about. Its presence is what makes one.</summary>
        public const string AccessControlRequestMethod = "Access-Control-Request-Method";

        public const string AccessControlRequestHeaders = "Access-Control-Request-Headers";
    }
}