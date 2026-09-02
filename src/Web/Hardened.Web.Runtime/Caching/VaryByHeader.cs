using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Caching;

/// <summary>
/// Keys the response on named request headers, and says so in <c>Vary</c>.
///
/// <code>
/// [Get("/catalog")]
/// [CacheResponse&lt;VaryByHeader&gt;("Accept-Language", Duration = 60)]
/// public Catalog Browse() =&gt; _catalog.Localized();
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>It writes <c>Vary</c> as well as reading the headers.</b> A response varying on a header that
/// does not say so is one a shared cache in front of this service will serve to the wrong caller,
/// and the header nobody remembers to write is exactly the one this attribute knows. ASP.NET Core's
/// <c>VaryByHeaderNames</c> does not do this.
/// </para>
/// <para>
/// A cookie is deliberately not a header this will vary on. <c>Cookie</c> carries a session, and a
/// response keyed on one is a response with one caller - which is a cache entry nothing will ever
/// hit and a name that reads as though it might.
/// </para>
/// </remarks>
public sealed class VaryByHeader : ICacheKeyProvider {

    private readonly string[] _names;
    private readonly StringValues _vary;

    private VaryByHeader(string[] names) {
        _names = names;
        _vary = new StringValues(string.Join(", ", names));
    }

    public static ICacheKeyProvider Create(string[] values) {
        if (values.Length == 0) {
            throw new ArgumentException(
                "VaryByHeader needs at least one header name to vary on.", nameof(values));
        }

        foreach (var name in values) {
            if (string.Equals(name, KnownHeaders.Cookie, StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException(
                    "VaryByHeader will not vary on Cookie. A response keyed on a session has one " +
                    "caller, so the entry is never hit. Write a key provider over the claim that " +
                    "actually varies the answer.",
                    nameof(values));
            }
        }

        return new VaryByHeader(values);
    }

    public ValueTask<string?> Key(IExecutionContext context) {
        context.Response.Headers[KnownHeaders.Vary] = _vary;

        var key = new StringBuilder();
        var headers = context.Request.Headers;

        foreach (var name in _names) {
            // The name as well as the value. Without it, two headers whose values concatenate the
            // same way compose one key.
            key.Append(name).Append('=').Append(Read(headers, name).ToString()).Append('&');
        }

        return new ValueTask<string?>(key.ToString());
    }

    /// <summary>
    /// A header value, looked up the way HTTP defines header names.
    /// </summary>
    /// <remarks>
    /// The dictionary a transport supplies is usually case-insensitive and is not required to be -
    /// API Gateway's HTTP API delivers names lowercased, and a forked request carries whatever
    /// dictionary it was handed. A cache key that reads a header as absent because the caller sent
    /// it in a different case is one entry per casing, which is worse than a scan of a collection
    /// this small.
    /// </remarks>
    private static StringValues Read(IDictionary<string, StringValues> headers, string name) {
        if (headers.TryGetValue(name, out var value)) {
            return value;
        }

        foreach (var header in headers) {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) {
                return header.Value;
            }
        }

        return StringValues.Empty;
    }
}
