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

    /// <summary>
    /// The validator for the representation being sent.
    /// </summary>
    /// <remarks>
    /// Without it on the response there is nothing for a client to put in
    /// <see cref="IfNoneMatch"/>, so a server that reads that header and never writes this one has
    /// a conditional-request path no browser can ever reach. That was the state of static content
    /// until this constant existed.
    /// </remarks>
    public const string ETag = "ETag";

    /// <summary>
    /// When the representation last changed. The validator a cache falls back on when it has no
    /// <see cref="ETag"/>, and the one an <see cref="IfModifiedSince"/> is compared against.
    /// </summary>
    public const string LastModified = "Last-Modified";

    /// <summary>The date-based conditional. Weaker than <see cref="IfNoneMatch"/>, and outranked by it.</summary>
    public const string IfModifiedSince = "If-Modified-Since";

    /// <summary>
    /// That ranges are understood at this resource, which is what makes a media element seek.
    /// </summary>
    /// <remarks>
    /// Its absence is not neutral: a client that cannot tell whether ranges work assumes they do
    /// not, so a video served without it plays from the start and cannot be scrubbed.
    /// </remarks>
    public const string AcceptRanges = "Accept-Ranges";

    /// <summary>The bytes being asked for.</summary>
    public const string Range = "Range";

    /// <summary>Which bytes a 206 carries, or the length a 416 was measured against.</summary>
    public const string ContentRange = "Content-Range";

    /// <summary>
    /// Whether the range still applies to what the client holds. A range against a representation
    /// that has since changed is a request for bytes that no longer mean what the client thinks.
    /// </summary>
    public const string IfRange = "If-Range";

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