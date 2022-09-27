namespace Hardened.Web.Runtime.CacheControl
{
    [Flags]
    public enum CacheControlEnum
    {
        MaxAge = 1,

        NoCache = 2,

        NoStore = 4,

        NoTransform = 8,

        Public = 32,

        Private = 64
    }
}
