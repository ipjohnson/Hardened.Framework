using System.Text;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Headers;

/// <summary>
/// A cookie collection that writes <c>Set-Cookie</c> onto the response headers, for hosts where
/// that is how a cookie reaches the client.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CookieSetCollectionImpl"/> only records: it stores into a dictionary and something
/// else is expected to serialise it. That works on API Gateway, whose response carries a
/// <c>cookies</c> array separate from its headers and whose processor reads the dictionary. On an
/// HTTP host nothing read it, so <c>Response.Cookies.Append(...)</c> compiled, ran, and never
/// reached the client.
/// </para>
/// <para>
/// Headers are written on append rather than flushed at the end, because a response's headers go
/// out when the body first writes — anything deferred past that is already too late.
/// </para>
/// </remarks>
public class HeaderCookieSetCollection : ICookieSetCollection {
    private static readonly IReadOnlyDictionary<string, Tuple<string, CookieSetOptions>> None =
        new Dictionary<string, Tuple<string, CookieSetOptions>>();

    private readonly IDictionary<string, StringValues> _headers;

    /// <summary>
    /// Created on the first cookie. Most responses set none, so the common path allocates nothing
    /// beyond this collection itself — and hosts create that lazily too.
    /// </summary>
    private Dictionary<string, Tuple<string, CookieSetOptions>>? _cookies;

    public HeaderCookieSetCollection(IDictionary<string, StringValues> headers) {
        _headers = headers;
    }

    public void Append(string cookieName, string cookieValue, CookieSetOptions? options = null) {
        _cookies ??= new Dictionary<string, Tuple<string, CookieSetOptions>>();
        _cookies[cookieName] =
            new Tuple<string, CookieSetOptions>(cookieValue, options ?? CookieSetOptions.Empty);

        WriteHeader();
    }

    /// <summary>
    /// Rewrites the whole header from the dictionary rather than appending one value.
    /// </summary>
    /// <remarks>
    /// Last write for a name wins — the semantic <see cref="CookieSetCollectionImpl"/> and the API
    /// Gateway collection both document. Appending would emit the replaced value as well, leaving
    /// the client to pick, and which one it picks is not something to leave to a client.
    /// </remarks>
    private void WriteHeader() {
        var values = new string[_cookies!.Count];
        var index = 0;

        foreach (var cookie in _cookies) {
            var builder = new StringBuilder();

            builder.Append(cookie.Key);
            builder.Append('=');
            builder.Append(cookie.Value.Item1);
            cookie.Value.Item2.AppendSettings(builder);

            values[index++] = builder.ToString();
        }

        _headers["Set-Cookie"] = new StringValues(values);
    }

    public IReadOnlyDictionary<string, Tuple<string, CookieSetOptions>> Cookies => _cookies ?? None;
}
