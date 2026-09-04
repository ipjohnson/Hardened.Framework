using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.QueryString;

/// <summary>
/// A raw query string, as the collection the pipeline binds parameters from.
/// </summary>
/// <remarks>
/// <para>
/// One implementation, because a second one drifts. This lived in <c>FeatureExecutionRequest</c>
/// and was duplicated - badly - in <c>TestWebApp</c>: the copy split on <c>'='</c> and stored the
/// raw substring, so it never decoded a percent-escape and dropped any pair whose value contained
/// an <c>'='</c>. A test driving <c>?asOf=2026-09-10T09%3A00%3A00%2B00%3A00</c> got a 400 through
/// the harness and a 200 on Kestrel, which is the one direction a test host must never fail in.
/// </para>
/// <para>
/// The ASP.NET Core adapter reads <c>HttpRequest.Query</c> instead, which the server has already
/// parsed and decoded. It is the third implementation only in the sense that the framework does not
/// write it.
/// </para>
/// </remarks>
public static class QueryStringParser {

    /// <summary>
    /// Parses a query string, with or without its leading <c>?</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Split on the <em>first</em> <c>'='</c> rather than on all of them: a value is allowed to
    /// contain one, and base64 routinely does. A pair with no <c>'='</c> at all is a flag, and binds
    /// as an empty value rather than being discarded.
    /// </para>
    /// <para>
    /// A repeated key keeps every value. It used to overwrite, so <c>?symbols=EUR&amp;symbols=GBP</c>
    /// arrived as <c>GBP</c> alone - and that is OpenAPI's default array style, so the loss happened
    /// before binding could see there was a list to bind.
    /// </para>
    /// </remarks>
    public static IQueryStringCollection Parse(string? rawQueryString) {
        if (string.IsNullOrEmpty(rawQueryString) || rawQueryString == "?") {
            return EmptyQueryStringCollection.Instance;
        }

        var trimmed = rawQueryString![0] == '?' ? rawQueryString.Substring(1) : rawQueryString;
        var values = new Dictionary<string, StringValues>();

        foreach (var pair in trimmed.Split('&')) {
            if (pair.Length == 0) {
                continue;
            }

            var separator = pair.IndexOf('=');

            if (separator > -1) {
                Add(values, Decode(pair.Substring(0, separator)), Decode(pair.Substring(separator + 1)));
            }
            else {
                Add(values, Decode(pair), "");
            }
        }

        return values.Count == 0 ? EmptyQueryStringCollection.Instance : new SimpleQueryStringCollection(values);
    }

    private static void Add(Dictionary<string, StringValues> values, string key, string value) {
        values[key] = values.TryGetValue(key, out var existing)
            ? StringValues.Concat(existing, value)
            : new StringValues(value);
    }

    /// <summary>
    /// Parses the query portion of a path, or an empty collection when it carries none.
    /// </summary>
    /// <remarks>
    /// For callers holding a whole request target rather than the query alone - the test host takes
    /// <c>"/reports/overdue?asOf=..."</c> as one string, where a server hands the two over already
    /// separated.
    /// </remarks>
    public static IQueryStringCollection ParseFromPath(string? path) {
        if (string.IsNullOrEmpty(path)) {
            return EmptyQueryStringCollection.Instance;
        }

        var questionMark = path!.IndexOf('?');

        return questionMark == -1
            ? EmptyQueryStringCollection.Instance
            : Parse(path.Substring(questionMark + 1));
    }

    /// <summary>
    /// Percent-decoding, with <c>'+'</c> read as a space.
    /// </summary>
    /// <remarks>
    /// <c>Uri.UnescapeDataString</c> alone leaves <c>'+'</c> as itself, which is right for a path
    /// segment and wrong here: a form-encoded query writes a space as <c>'+'</c>, and every server
    /// this framework runs behind decodes it that way. Replaced before unescaping, so a literal
    /// plus that arrived as <c>%2B</c> survives.
    /// </remarks>
    private static string Decode(string value) {
        if (value.Length == 0) {
            return value;
        }

        if (value.IndexOf('+') > -1) {
            value = value.Replace('+', ' ');
        }

        return value.IndexOf('%') > -1 ? Uri.UnescapeDataString(value) : value;
    }
}
