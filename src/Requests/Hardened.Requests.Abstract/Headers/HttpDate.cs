using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

/// <summary>
/// The one date format HTTP writes, and what it accepts back.
/// </summary>
/// <remarks>
/// <para>
/// RFC 9110 §5.6.7: a sender writes the fixed-length form, <c>Sun, 06 Nov 1994 08:49:37 GMT</c>,
/// and a recipient also accepts the RFC 850 and asctime forms that older software still sends.
/// <see cref="TryParse"/> tries the fixed form first, because it is what every client echoes back
/// from a <c>Last-Modified</c> this framework wrote, then the two obsolete forms, and last the
/// invariant culture's own parser - which is what the static content writer accepted before this
/// existed, and is kept so that a date it read then is still read now.
/// </para>
/// <para>
/// <b>Whole seconds.</b> The format has no finer precision, so a modification time carrying
/// milliseconds is always later than the copy of itself a client was given, and every conditional
/// request against it misses by up to 999 milliseconds forever. <see cref="Truncate"/> is what a
/// comparison applies to its own side first.
/// </para>
/// </remarks>
public static class HttpDate {

    private const string Fixed = "R";

    /// <summary>
    /// The three forms of §5.6.7, fixed-length first. asctime pads a single-digit day with a
    /// space, which is the inner whitespace the styles allow.
    /// </summary>
    private static readonly string[] Forms = [
        Fixed,
        "dddd, dd-MMM-yy HH:mm:ss 'GMT'",
        "ddd MMM d HH:mm:ss yyyy"
    ];

    private const DateTimeStyles Utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    private const DateTimeStyles Exact = Utc | DateTimeStyles.AllowWhiteSpaces;

    /// <summary>
    /// <paramref name="when"/> as an HTTP-date.
    /// </summary>
    /// <remarks>
    /// Converted to UTC first. The format writes <c>GMT</c> whatever offset the value carries and
    /// does not shift the time to match, so a local time formatted directly is wrong by the offset.
    /// </remarks>
    public static string Format(DateTimeOffset when) =>
        when.ToUniversalTime().ToString(Fixed, CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="when"/> in UTC with anything below a second removed, which is all the
    /// header can carry.
    /// </summary>
    public static DateTimeOffset Truncate(DateTimeOffset when) {
        var utc = when.ToUniversalTime();

        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerSecond));
    }

    /// <summary>
    /// Reads an HTTP-date, or answers false for anything that is not one.
    /// </summary>
    /// <remarks>
    /// One member exactly. RFC 9110 §13.1.3 says to ignore a conditional header that arrived with
    /// more than one, rather than pick between them; and a malformed date is ignored rather than
    /// refused, because a value nobody can parse says nothing about what the client holds.
    /// </remarks>
    public static bool TryParse(StringValues value, out DateTimeOffset parsed) {
        parsed = default;

        if (value.Count != 1) {
            return false;
        }

        var text = value[0];

        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        return DateTimeOffset.TryParseExact(text, Forms, CultureInfo.InvariantCulture, Exact, out parsed) ||
               DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, Utc, out parsed);
    }
}
