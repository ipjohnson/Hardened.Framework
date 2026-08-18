using System.Globalization;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// The tests a route token's constraint compiles to.
/// </summary>
/// <remarks>
/// <para>
/// <b>A constraint is a guarantee, not a selector.</b> <c>/users/{id}</c> with an <c>int id</c>
/// answers <c>/users/abc</c> with 400 - the route matched and the binder failed.
/// <c>/users/{id:int}</c> makes it a 404, which is the truthful answer: there is no resource at
/// that URL, and 400 implies you addressed a real endpoint incorrectly. It also rejects garbage
/// before any filter or binder runs.
/// </para>
/// <para>
/// The rule for what belongs here is not "what can we convert" but "what can be tested on a
/// <see cref="ReadOnlySpan{T}"/> without allocating". A constraint runs on every request that
/// reaches the position it guards, including the ones it rejects, so anything that allocates to
/// decide would make the failure path more expensive than the success path.
/// </para>
/// <para>
/// Invariant culture throughout. A route is part of a URL, which is the same string in every
/// locale - parsing <c>{id:int}</c> under a culture with a different negative sign would make the
/// same request match on one machine and not another.
/// </para>
/// </remarks>
public static class RouteConstraints {

    /// <summary>The names a route template may use, and what each compiles to.</summary>
    public const string Int = "int";

    public const string Long = "long";

    public const string Guid = "guid";

    public const string Bool = "bool";

    public const string Decimal = "decimal";

    public const string Date = "date";

    public const string DateTime = "datetime";

    public const string Alpha = "alpha";

    public const string Slug = "slug";

    public const string Hex = "hex";

    public static bool IsInt(ReadOnlySpan<char> value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static bool IsLong(ReadOnlySpan<char> value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static bool IsGuid(ReadOnlySpan<char> value) =>
        System.Guid.TryParse(value, out _);

    public static bool IsBool(ReadOnlySpan<char> value) =>
        bool.TryParse(value, out _);

    public static bool IsDecimal(ReadOnlySpan<char> value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    /// <summary>An ISO 8601 calendar date - <c>yyyy-MM-dd</c>, and nothing else.</summary>
    /// <remarks>
    /// <see cref="DateOnly.TryParseExact(ReadOnlySpan{char}, ReadOnlySpan{char}, out DateOnly)"/>
    /// rather than <c>TryParse</c>, which accepts a large and culture-sensitive grammar. A route that
    /// matched <c>12/06/2026</c> while disagreeing about which number was the month would select a
    /// different handler on different machines.
    /// </remarks>
    public static bool IsDate(ReadOnlySpan<char> value) =>
        DateOnly.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>An ISO 8601 date and time, in one of <see cref="Iso8601.Formats"/>.</summary>
    /// <remarks>Exact formats only, for the reason <see cref="IsDate"/> gives.</remarks>
    public static bool IsDateTime(ReadOnlySpan<char> value) =>
        DateTimeOffset.TryParseExact(
            value, Iso8601.Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary><c>^[A-Za-z]+$</c>. ASCII, because a route is part of a URL.</summary>
    public static bool IsAlpha(ReadOnlySpan<char> value) {
        if (value.Length == 0) {
            return false;
        }

        foreach (var character in value) {
            if (!char.IsAsciiLetter(character)) {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>^[0-9a-fA-F]+$</c> — a content hash, a commit sha, a request id.</summary>
    public static bool IsHex(ReadOnlySpan<char> value) {
        if (value.Length == 0) {
            return false;
        }

        foreach (var character in value) {
            if (!char.IsAsciiHexDigit(character)) {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>^[a-z0-9]+(-[a-z0-9]+)*$</c> — no leading, trailing or doubled hyphen.</summary>
    /// <remarks>
    /// Lower case only. A slug is a canonical form: admitting <c>My-Post</c> beside <c>my-post</c>
    /// would make two URLs for one resource, which is the thing a slug exists to avoid.
    /// </remarks>
    public static bool IsSlug(ReadOnlySpan<char> value) {
        if (value.Length == 0 || value[0] == '-' || value[value.Length - 1] == '-') {
            return false;
        }

        var previousWasHyphen = false;

        foreach (var character in value) {
            if (character == '-') {
                if (previousWasHyphen) {
                    return false;
                }

                previousWasHyphen = true;
                continue;
            }

            if (!char.IsAsciiDigit(character) && !char.IsAsciiLetterLower(character)) {
                return false;
            }

            previousWasHyphen = false;
        }

        return true;
    }

    /// <summary>
    /// The formats <see cref="IsDateTime"/> accepts, on a nested type so that only a route declaring
    /// <c>:datetime</c> pays for them.
    /// </summary>
    /// <remarks>
    /// A static field on <see cref="RouteConstraints"/> itself would make the whole class carry a
    /// type initialiser. C# emits <c>beforefieldinit</c> for a class with only static field
    /// initialisers, so <see cref="IsInt"/> would very probably still be free — but "very probably
    /// free" is not the claim this class makes. Nested, the array is reachable only from the one
    /// method that reads it, and a trimmer drops it with that method.
    /// </remarks>
    private static class Iso8601 {
        public static readonly string[] Formats = {
            "yyyy-MM-ddTHH:mm:ssK",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
            "yyyy-MM-ddTHH:mmK",
            "yyyy-MM-dd"
        };
    }
}
