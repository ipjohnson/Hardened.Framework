using Hardened.Requests.Abstract.Forms;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// Parses <c>name=value&amp;name=value</c>.
/// </summary>
/// <remarks>
/// <para>
/// The same format a query string uses, which is why <c>FeatureExecutionRequest.ParseQueryString</c>
/// reads almost identically. It is deliberately not shared with that one: the query parser produces
/// <c>IQueryStringCollection</c> over a <c>string</c> dictionary and lives in a transport adapter,
/// and merging the two would drag a transport concern into the runtime for the sake of twenty
/// lines that differ where it matters.
/// </para>
/// <para>
/// <b>Where it matters is <c>+</c>.</b> In an
/// <c>application/x-www-form-urlencoded</c> body a space is encoded as <c>+</c>, and
/// <c>Uri.UnescapeDataString</c> does not decode it - it handles <c>%20</c> and leaves a plus
/// alone. A parser shared with the query string would therefore bind <c>"John+Smith"</c> for a
/// field a browser sent as <c>John Smith</c>, on every form post, silently.
/// </para>
/// </remarks>
public static class UrlEncodedParser {

    /// <summary>
    /// Parses a form body into its fields.
    /// </summary>
    /// <remarks>
    /// Repeated names accumulate rather than overwrite: a form with several checkboxes of one name
    /// sends the name once per checked box, and taking the last would silently drop the rest.
    /// <see cref="StringValues"/> is what the header collections already use for the same reason.
    /// </remarks>
    public static IFormCollection Parse(string? body) {
        if (string.IsNullOrEmpty(body)) {
            return EmptyFormCollection.Instance;
        }

        var fields = new Dictionary<string, StringValues>(StringComparer.Ordinal);

        foreach (var pair in body!.Split('&')) {
            if (pair.Length == 0) {
                continue;
            }

            var separator = pair.IndexOf('=');

            var name = separator > -1 ? Decode(pair.Substring(0, separator)) : Decode(pair);
            var value = separator > -1 ? Decode(pair.Substring(separator + 1)) : "";

            fields[name] = fields.TryGetValue(name, out var existing)
                ? StringValues.Concat(existing, value)
                : new StringValues(value);
        }

        return fields.Count == 0 ? EmptyFormCollection.Instance : new SimpleFormCollection(fields);
    }

    /// <summary>
    /// Percent-decoding, plus the <c>+</c> a form uses for a space.
    /// </summary>
    /// <remarks>
    /// The replacement runs first. Doing it after unescaping would turn a literal plus the sender
    /// escaped as <c>%2B</c> into a space, which is the one case the escape exists to prevent.
    /// </remarks>
    private static string Decode(string value) =>
        value.IndexOf('+') > -1
            ? Uri.UnescapeDataString(value.Replace('+', ' '))
            : Uri.UnescapeDataString(value);
}
