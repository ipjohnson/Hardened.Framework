namespace Hardened.Web.Runtime.Headers
{
    public static class KnownHeaders
    {
        public const string Accept = "Accept";

        public const string AcceptEncoding = "AcceptEncoding";

        public const string CacheControl = "Cache-Control";

        public const string ContentEncoding = "Content-Encoding";

        public const string ContentType = "Content-Type";

        public const string ContentLength = "Content-Length";
        
        public const string IfMatch = "If-Match";

        public static class Cors
        {
            public const string AccessControlAllowOrigin = "Access-Control-Allow-Origin";

            public const string AccessControlAllowHeaders = "Access-Control-Allow-Headers";
        }
    }
}
