using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

/// <summary>
/// Whether a request said it takes a particular content coding.
/// </summary>
/// <remarks>
/// <para>
/// <b>The obvious spelling is wrong, and was shipping.</b> <c>Accept-Encoding</c> arrives as one
/// header value listing several codings - <c>gzip, deflate, br, zstd</c> is what a browser sends -
/// so a <see cref="StringValues"/> holding it has a single element, and
/// <c>values.Contains("gzip")</c> is element equality against that whole string. It answers false
/// for every real browser and true only for a client that sent the coding on its own.
/// <c>StaticContentHandler</c> asked exactly that question, so no browser ever received one of its
/// pre-compressed assets: every one of them took the decompress-on-the-way-out path instead.
/// </para>
/// <para>
/// A search within the value, then, bounded on both sides so <c>x-gzip</c> and a coding merely
/// containing the letters do not match. Lifted from <c>OpenApiDocumentProvider</c>, which had the
/// only correct implementation of this in the codebase and had it private.
/// </para>
/// <para>
/// The quality value is ignored, which is the behaviour that provider documented and this preserves:
/// <c>gzip;q=0</c> is rare enough not to be worth a parser, and the cost of being wrong is a response
/// compressed for a client that would rather have had it plain.
/// </para>
/// </remarks>
public static class AcceptEncodingHeader {

    /// <summary>
    /// Whether <paramref name="acceptEncoding"/> names <paramref name="coding"/>.
    /// </summary>
    public static bool Accepts(StringValues acceptEncoding, string coding) {
        if (string.IsNullOrEmpty(coding)) {
            return false;
        }

        foreach (var value in acceptEncoding) {
            if (value == null) {
                continue;
            }

            var index = value.IndexOf(coding, StringComparison.OrdinalIgnoreCase);

            while (index >= 0) {
                var after = index + coding.Length;

                if ((index == 0 || !IsTokenCharacter(value[index - 1])) &&
                    (after == value.Length || !IsTokenCharacter(value[after]))) {
                    return true;
                }

                index = value.IndexOf(coding, index + 1, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    /// <summary>
    /// Whether this character continues a coding name, so <c>x-gzip</c> and a hypothetical
    /// <c>gzip2</c> are one token rather than a match plus a neighbour.
    /// </summary>
    private static bool IsTokenCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '-';
}
