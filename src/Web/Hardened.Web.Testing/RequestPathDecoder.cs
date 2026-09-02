using System.Text;

namespace Hardened.Web.Testing;

/// <summary>
/// Percent-decodes a request path the way a transport hands one over.
/// </summary>
/// <remarks>
/// <para>
/// The harness's whole value is that it drives the real pipeline, so a request that answers 404
/// here and 400 on Kestrel is worse than no harness for that case. <c>app.Get("/events/%20")</c>
/// reached the handler as the literal <c>%20</c> and matched nothing, while the same request over
/// a socket decoded to whitespace and reached the validator.
/// </para>
/// <para>
/// <b>The rule is Kestrel's, measured rather than assumed.</b> Probed against the Kestrel
/// integration application over a real socket:
/// </para>
/// <code>
/// /echo/path/%20         -> " "
/// /echo/path/caf%C3%A9   -> "café"
/// /echo/path/a%5Cb       -> "a\b"
/// /echo/path/a%25b       -> "a%b"
/// /echo/path/a%2Fb       -> "a%2Fb"   the one escape that stays
/// /echo/path/a+b         -> "a+b"     a plus is a plus in a path
/// /echo/path/a%zz        -> "a%zz"    not an escape at all
/// </code>
/// <para>
/// <c>%2F</c> survives because decoding it would put a separator inside a segment, which would
/// change how many segments the path has. <c>Uri.UnescapeDataString</c> decodes it, which is why
/// this is written out rather than delegated.
/// </para>
/// </remarks>
internal static class RequestPathDecoder {

    public static string Decode(string path) {
        if (path.IndexOf('%') < 0) {
            return path;
        }

        var builder = new StringBuilder(path.Length);
        List<byte>? pending = null;

        for (var index = 0; index < path.Length; index++) {
            if (path[index] == '%' && index + 2 < path.Length &&
                Hex(path[index + 1]) is { } high && Hex(path[index + 2]) is { } low) {
                var value = (byte)((high << 4) | low);

                // A separator inside a segment would change how many segments the path has, so
                // this one escape is left as the caller wrote it.
                if (value == (byte)'/') {
                    Flush(builder, ref pending);
                    builder.Append(path, index, 3);
                }
                else {
                    (pending ??= new List<byte>()).Add(value);
                }

                index += 2;

                continue;
            }

            Flush(builder, ref pending);
            builder.Append(path[index]);
        }

        Flush(builder, ref pending);

        return builder.ToString();
    }

    /// <summary>
    /// Decoded bytes become characters together, because one character may take several of them.
    /// </summary>
    private static void Flush(StringBuilder builder, ref List<byte>? pending) {
        if (pending == null || pending.Count == 0) {
            return;
        }

        builder.Append(Encoding.UTF8.GetString(pending.ToArray()));
        pending.Clear();
    }

    private static int? Hex(char character) => character switch {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => null
    };
}
