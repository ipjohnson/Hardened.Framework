using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Execution;

/// <summary>
/// Cookie access for generated request binding code.
/// </summary>
/// <remarks>
/// <para>
/// <c>IExecutionRequest.Cookies</c> is the raw strings a transport received, not a lookup - so a
/// cookie parameter had nothing to bind through, which is why <c>in: cookie</c> was parsed and then
/// dropped. This supplies the same <c>Get</c> the other three collections carry, so generated
/// binding stays one shape across path, query, header and cookie.
/// </para>
/// <para>
/// An entry may be a whole <c>Cookie</c> header - <c>a=1; b=2</c> - or a single pair, depending on
/// the transport, so both are handled. Values are returned exactly as received: nothing else in the
/// binding path decodes, and decoding here would make a cookie the one input that arrives
/// transformed.
/// </para>
/// <para>
/// This namespace is already imported by generated handlers, which is why the extension lives here
/// rather than alongside the cookie abstractions.
/// </para>
/// </remarks>
public static class CookieCollectionExtensions {

    public static StringValues Get(this IReadOnlyList<string> cookies, string name) {
        if (cookies == null) {
            return StringValues.Empty;
        }

        for (var i = 0; i < cookies.Count; i++) {
            var entry = cookies[i];

            if (string.IsNullOrEmpty(entry)) {
                continue;
            }

            var start = 0;

            while (start < entry.Length) {
                var end = entry.IndexOf(';', start);

                if (end < 0) {
                    end = entry.Length;
                }

                var separator = entry.IndexOf('=', start);

                if (separator > start && separator < end) {
                    var key = entry.Substring(start, separator - start).Trim();

                    if (string.Equals(key, name, StringComparison.Ordinal)) {
                        return entry.Substring(separator + 1, end - separator - 1).Trim();
                    }
                }

                start = end + 1;
            }
        }

        return StringValues.Empty;
    }
}
