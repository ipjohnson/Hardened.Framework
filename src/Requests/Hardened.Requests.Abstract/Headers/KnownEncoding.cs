using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

public static class KnownEncoding
{
    public const string GZip = "gzip";
    public static readonly StringValues GZipStringValues = new StringValues(GZip);

    public const string Br = "br";
    public static readonly StringValues BrStringValues = new StringValues(Br);
}