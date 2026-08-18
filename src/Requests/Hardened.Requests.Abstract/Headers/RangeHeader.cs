using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

/// <summary>
/// The bytes a <c>Range</c> header asks for, resolved against a known length.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>bytes</c>, and only one range. Multiple ranges are legal and are answered with the whole
/// entity instead: <c>multipart/byteranges</c> is a second body format, a second content type and a
/// boundary generator, and the clients that drive this - a media element seeking, a download
/// resuming - all send one range. Answering 200 to a multi-range request is explicitly allowed,
/// and a client that asked for several is required to cope with getting everything.
/// </para>
/// <para>
/// Parsing is deliberately strict. RFC 9110 §14.2 says a range that cannot be parsed must be
/// ignored - the response is the whole entity, not an error - so every malformed shape resolves to
/// "no range" rather than to a 416. A 416 is reserved for a range that parsed and cannot be
/// satisfied, which is a different statement: the client asked for a byte past the end.
/// </para>
/// </remarks>
public readonly record struct ByteRange(long From, long To) {

    /// <summary>How many bytes this range covers.</summary>
    public long Length => To - From + 1;

    /// <summary>The <c>Content-Range</c> for a 206 carrying this range out of <paramref name="totalLength"/>.</summary>
    public string ContentRange(long totalLength) =>
        $"bytes {From}-{To}/{totalLength}";

    /// <summary>
    /// The <c>Content-Range</c> for a 416, which names the length rather than a range.
    /// </summary>
    /// <remarks>
    /// Required on a 416 and the only useful thing on it: it is how a client that guessed wrong
    /// learns what to ask for instead, without a second round trip to find out.
    /// </remarks>
    public static string Unsatisfied(long totalLength) => $"bytes */{totalLength}";
}

/// <summary>
/// How a <c>Range</c> header resolved.
/// </summary>
public enum RangeResult {

    /// <summary>No range was asked for, or one was asked for in a form that must be ignored.</summary>
    None,

    /// <summary>One range, and it fits.</summary>
    Satisfiable,

    /// <summary>A range that parsed and starts past the end. Answered 416.</summary>
    Unsatisfiable
}

public static class RangeHeader {

    private const string BytesUnit = "bytes";

    /// <summary>The only range unit anything sends, and the only one worth advertising.</summary>
    public static readonly StringValues AcceptsBytes = new(BytesUnit);

    /// <summary>
    /// Resolves <paramref name="range"/> against a representation of <paramref name="totalLength"/>
    /// bytes.
    /// </summary>
    /// <remarks>
    /// A zero-length representation cannot satisfy any range at all, so every range over one is
    /// unsatisfiable - including <c>bytes=0-</c>, which reads as "everything" and where there is no
    /// everything to give.
    /// </remarks>
    public static RangeResult Resolve(StringValues range, long totalLength, out ByteRange resolved) {
        resolved = default;

        if (range.Count == 0) {
            return RangeResult.None;
        }

        var value = range.ToString();

        if (string.IsNullOrWhiteSpace(value)) {
            return RangeResult.None;
        }

        var span = value.AsSpan().Trim();

        if (!span.StartsWith(BytesUnit.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
            return RangeResult.None;
        }

        span = span.Slice(BytesUnit.Length).TrimStart();

        if (span.Length == 0 || span[0] != '=') {
            return RangeResult.None;
        }

        span = span.Slice(1).Trim();

        // More than one range. Legal, and answered with the whole entity - see the note on
        // ByteRange. Detected before parsing so the first range is not served as if it were the
        // only one asked for, which would be a 206 the client did not request.
        if (span.IndexOf(',') >= 0) {
            return RangeResult.None;
        }

        var dash = span.IndexOf('-');

        if (dash < 0) {
            return RangeResult.None;
        }

        var firstText = span.Slice(0, dash).Trim();
        var lastText = span.Slice(dash + 1).Trim();

        // "-500" is the final 500 bytes, not a range starting at minus five hundred. It is the
        // shape a client uses when it knows how much tail it wants and not how long the whole is.
        if (firstText.Length == 0) {
            if (!TryParse(lastText, out var suffixLength) || suffixLength <= 0) {
                return RangeResult.None;
            }

            if (totalLength == 0) {
                return RangeResult.Unsatisfiable;
            }

            var from = Math.Max(0, totalLength - suffixLength);

            resolved = new ByteRange(from, totalLength - 1);

            return RangeResult.Satisfiable;
        }

        if (!TryParse(firstText, out var start)) {
            return RangeResult.None;
        }

        // Past the end is the one thing that is an error rather than an omission: the client asked
        // for bytes that do not exist, and telling it the length is more useful than sending the
        // whole entity it did not want.
        if (start >= totalLength) {
            return RangeResult.Unsatisfiable;
        }

        if (lastText.Length == 0) {
            resolved = new ByteRange(start, totalLength - 1);

            return RangeResult.Satisfiable;
        }

        if (!TryParse(lastText, out var end)) {
            return RangeResult.None;
        }

        if (end < start) {
            return RangeResult.None;
        }

        resolved = new ByteRange(start, Math.Min(end, totalLength - 1));

        return RangeResult.Satisfiable;
    }

    private static bool TryParse(ReadOnlySpan<char> text, out long value) {
        value = 0;

        if (text.Length == 0) {
            return false;
        }

        foreach (var character in text) {
            if (character is < '0' or > '9') {
                return false;
            }
        }

        return long.TryParse(text, out value);
    }
}
