using System.Text;

namespace Hardened.Web.Runtime.CacheControl;

/// <summary>
/// A <see cref="CacheControlEnum"/> as the value of a <c>Cache-Control</c> header.
/// </summary>
/// <remarks>
/// <para>
/// One formatter, because there were about to be two. The static content path built its own header
/// inline and ignored the <c>CacheControlType</c> its configuration carried, so it could not express
/// <c>no-store</c>, <c>no-cache</c>, <c>public</c>, <c>private</c> or <c>no-transform</c> at all.
/// <c>StaticContentWriter.CacheControlFor</c> renders through this now, which is the retrofit this
/// was moved here to make possible.
/// </para>
/// <para>
/// Every flag the caller set is rendered, including combinations a cache will read as
/// contradictory - <c>no-store</c> alongside a <c>max-age</c>, say. Dropping one would be this
/// framework deciding what the author meant, and an attribute that silently emits something other
/// than what it says is the thing this exists to stop.
/// </para>
/// </remarks>
public static class CacheControlHeader {

    /// <summary>
    /// The header value, or null when no directive is set and the header should be omitted.
    /// </summary>
    /// <param name="type">The directives to render.</param>
    /// <param name="maxAge">
    /// Seconds for <c>max-age</c>. Rendered only when <see cref="CacheControlEnum.MaxAge"/> is set,
    /// so the flag decides whether the value appears rather than the value deciding for itself -
    /// which is what lets <c>[CacheControl(Type = CacheControlEnum.NoStore)]</c> emit <c>no-store</c>
    /// alone rather than <c>no-store, max-age=0</c>.
    /// </param>
    /// <param name="immutable">
    /// Appends <c>immutable</c>. Separate from the flags because the enum has no member for it, and
    /// the static content configuration carries it as its own property.
    /// </param>
    public static string? Format(CacheControlEnum type, int maxAge, bool immutable = false) {
        var builder = new StringBuilder();

        // public and private are mutually exclusive. Both set is a contradiction the type system
        // allows, and private is the safer reading of it.
        if (type.HasFlag(CacheControlEnum.Private)) {
            Append(builder, "private");
        }
        else if (type.HasFlag(CacheControlEnum.Public)) {
            Append(builder, "public");
        }

        if (type.HasFlag(CacheControlEnum.NoCache)) {
            Append(builder, "no-cache");
        }

        if (type.HasFlag(CacheControlEnum.NoStore)) {
            Append(builder, "no-store");
        }

        if (type.HasFlag(CacheControlEnum.MaxAge)) {
            Append(builder, "max-age=" + maxAge.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (type.HasFlag(CacheControlEnum.NoTransform)) {
            Append(builder, "no-transform");
        }

        if (immutable) {
            Append(builder, "immutable");
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static void Append(StringBuilder builder, string directive) {
        if (builder.Length > 0) {
            builder.Append(", ");
        }

        builder.Append(directive);
    }
}
