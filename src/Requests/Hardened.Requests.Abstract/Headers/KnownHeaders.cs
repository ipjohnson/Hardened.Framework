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

    public const string IfMatch = "If-Match";

    public const string IfNoneMatch = "If-None-Match";

    public static class Cors {
        public const string AccessControlAllowOrigin = "Access-Control-Allow-Origin";

        public const string AccessControlAllowHeaders = "Access-Control-Allow-Headers";

        public const string AccessControlAllowMethods = "Access-Control-Allow-Methods";

        public const string AccessControlMaxAge = "Access-Control-Max-Age";
    }
}