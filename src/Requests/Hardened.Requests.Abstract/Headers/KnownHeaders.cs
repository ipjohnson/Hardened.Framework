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

    /// <summary>
    /// The id of the last event a reconnecting <c>EventSource</c> received.
    /// </summary>
    /// <remarks>
    /// Sent by the client on its own, on every reconnect, carrying the last <c>id:</c> field it
    /// saw. A streaming handler that binds it can resume after that event rather than replaying
    /// from the start. Hosts deliver header names in whatever case they like - API Gateway and a
    /// function URL lower-case them - and the header collection matches without regard to case.
    /// </remarks>
    public const string LastEventId = "Last-Event-ID";

    /// <summary>
    /// Whether an nginx in front of this response may buffer it. Everything else ignores it.
    /// </summary>
    /// <remarks>
    /// <c>no</c> turns proxy buffering off for one response, which is what lets a streamed body
    /// reach the client as it is written rather than when the proxy's buffer fills. Written on
    /// every event stream, because a stream that works locally and stalls behind a default nginx
    /// is otherwise a support ticket.
    /// </remarks>
    public const string XAccelBuffering = "X-Accel-Buffering";

    public const string ContentEncoding = "Content-Encoding";

    public const string ContentType = "Content-Type";

    public const string ContentLength = "Content-Length";

    public const string Cookie = "Cookie";

    /// <summary>
    /// A cookie the response sets.
    /// </summary>
    /// <remarks>
    /// Named here because it must never be replayed to a second caller: it identifies one. The
    /// response cache drops it as a response is captured, along with the hop-by-hop headers below.
    /// </remarks>
    public const string SetCookie = "Set-Cookie";

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

    /// <summary>
    /// How the body is framed on this connection.
    /// </summary>
    /// <remarks>
    /// Hop-by-hop: it describes one connection rather than the representation travelling over it,
    /// and RFC 9110 forbids storing or forwarding one. Named here because a stored copy of it is
    /// what made every response-cache hit malformed - the host declared chunked framing on the way
    /// in, the entry kept it, and the replayed bytes went out with no chunk header and no
    /// terminator.
    /// </remarks>
    public const string TransferEncoding = "Transfer-Encoding";

    /// <summary>
    /// What this connection does when the message ends, and the header that names which others are
    /// hop-by-hop.
    /// </summary>
    public const string Connection = "Connection";

    /// <summary>Hop-by-hop. How long an idle connection is held open.</summary>
    public const string KeepAlive = "Keep-Alive";

    /// <summary>Hop-by-hop. The transfer codings this connection will accept.</summary>
    public const string TE = "TE";

    /// <summary>Hop-by-hop. Which fields arrive after a chunked body.</summary>
    public const string Trailer = "Trailer";

    /// <summary>Hop-by-hop. The protocol this connection could switch to.</summary>
    public const string Upgrade = "Upgrade";

    /// <summary>Hop-by-hop. A challenge from the proxy, not from the origin.</summary>
    public const string ProxyAuthenticate = "Proxy-Authenticate";

    /// <summary>Hop-by-hop. A credential for the proxy, not for the origin.</summary>
    public const string ProxyAuthorization = "Proxy-Authorization";

    /// <summary>
    /// When the message was generated.
    /// </summary>
    /// <remarks>
    /// Written per response by the host. A stored copy replayed later dates a response to the
    /// moment a different one was produced, which is the value every downstream cache computes an
    /// age from.
    /// </remarks>
    public const string Date = "Date";

    /// <summary>What produced the response. The host's to write, and the same on all of them.</summary>
    public const string Server = "Server";

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