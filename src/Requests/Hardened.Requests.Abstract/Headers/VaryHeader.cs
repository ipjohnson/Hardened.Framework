using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

/// <summary>
/// Adds a request header name to <c>Vary</c> without losing the ones already there.
/// </summary>
/// <remarks>
/// <para>
/// Three things write <c>Vary</c>: the CORS filter, <c>VaryByHeader</c> and the response
/// compression filter. Each used to assign the header, so whichever ran last won and a
/// cross-origin cached response could say <c>Vary: Accept-Language</c> and nothing about
/// <c>Origin</c> - which is precisely the response a shared cache serves to the wrong origin.
/// Merging is the only correct operation on this header, so it lives in one place.
/// </para>
/// <para>
/// A <c>Vary: *</c> already covers every request header, and is left alone.
/// </para>
/// </remarks>
public static class VaryHeader {
    /// <summary>
    /// Ensures <paramref name="name"/> is listed in the response's <c>Vary</c> header.
    /// </summary>
    public static void Add(IDictionary<string, StringValues> headers, string name) {
        if (!headers.TryGetValue(KnownHeaders.Vary, out var existing) || StringValues.IsNullOrEmpty(existing)) {
            headers[KnownHeaders.Vary] = name;

            return;
        }

        // One value joined with the separator the header uses on the wire, so a Vary that arrived
        // as two values leaves as one and is compared the same way whoever reads it.
        var joined = string.Join(", ", existing.ToArray()!);

        foreach (var token in joined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (token == "*" || string.Equals(token, name, StringComparison.OrdinalIgnoreCase)) {
                headers[KnownHeaders.Vary] = joined;

                return;
            }
        }

        headers[KnownHeaders.Vary] = joined + ", " + name;
    }
}
