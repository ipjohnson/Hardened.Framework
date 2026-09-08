using System.Net;
using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Testing;

/// <summary>
/// An <see cref="HttpRequestData"/> a test can build outside the worker.
/// </summary>
/// <remarks>
/// <para>
/// The worker's request is an abstract class its gRPC layer implements from the host's invocation
/// request, and nothing public builds one - but nothing stops a subclass, which is what makes an
/// HTTP function testable without the worker. This is the same shape with the pieces a test can
/// supply: the method, the URL as the host would have built it, the headers, and the body as a
/// stream.
/// </para>
/// <para>
/// The cookies are read off the <c>Cookie</c> header, which is where the host reads them from.
/// The identities are empty, because an anonymous function has none.
/// </para>
/// </remarks>
public sealed class TestHttpRequestData : HttpRequestData {
    public TestHttpRequestData(
        FunctionContext context,
        string method,
        Uri url,
        IEnumerable<KeyValuePair<string, StringValues>>? headers = null,
        Stream? body = null)
        : base(context) {
        Method = method;
        Url = url;
        Body = body ?? Stream.Null;
        Headers = new HttpHeadersCollection();

        if (headers != null) {
            foreach (var header in headers) {
                Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        Cookies = ReadCookies(Headers);
    }

    public override Stream Body { get; }

    public override HttpHeadersCollection Headers { get; }

    public override IReadOnlyCollection<IHttpCookie> Cookies { get; }

    public override Uri Url { get; }

    public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();

    public override string Method { get; }

    public override HttpResponseData CreateResponse() => new TestHttpResponseData(FunctionContext);

    /// <summary>
    /// <c>name=value</c> pairs off every <c>Cookie</c> header, separated by semicolons, which is
    /// the whole of the request-side cookie syntax.
    /// </summary>
    private static IReadOnlyCollection<IHttpCookie> ReadCookies(HttpHeadersCollection headers) {
        if (!headers.TryGetValues("Cookie", out var values)) {
            return Array.Empty<IHttpCookie>();
        }

        var cookies = new List<IHttpCookie>();

        foreach (var header in values) {
            foreach (var pair in header.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                var equals = pair.IndexOf('=');

                if (equals <= 0) {
                    continue;
                }

                cookies.Add(new HttpCookie(
                    pair.Substring(0, equals).Trim(),
                    WebUtility.UrlDecode(pair.Substring(equals + 1).Trim())));
            }
        }

        return cookies;
    }
}

/// <summary>
/// The response a <see cref="TestHttpRequestData"/> creates: a status, headers, cookies and a
/// buffer, which is what the adapter fills and the test host reads back.
/// </summary>
public sealed class TestHttpResponseData : HttpResponseData {
    public TestHttpResponseData(FunctionContext context) : base(context) {
        Headers = new HttpHeadersCollection();
        Body = new MemoryStream();
        Cookies = new TestHttpCookies();
    }

    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public override HttpHeadersCollection Headers { get; set; }

    public override Stream Body { get; set; }

    public override HttpCookies Cookies { get; }
}

/// <summary>
/// The cookies a response appends, kept as a list so a test can read them back.
/// </summary>
/// <remarks>
/// The worker's own collection renders each into a <c>Set-Cookie</c> header on the way to the
/// host; this keeps the cookies themselves, and <see cref="Render"/> is the same header text for
/// a test that wants to see what a browser would.
/// </remarks>
public sealed class TestHttpCookies : HttpCookies {
    private readonly List<IHttpCookie> _cookies = new();

    public IReadOnlyList<IHttpCookie> Cookies => _cookies;

    public override void Append(string name, string value) => _cookies.Add(new HttpCookie(name, value));

    public override void Append(IHttpCookie cookie) => _cookies.Add(cookie);

    public override IHttpCookie CreateNew() => new HttpCookie("", "");

    /// <summary>The cookie as a <c>Set-Cookie</c> header value.</summary>
    public static string Render(IHttpCookie cookie) {
        var builder = new System.Text.StringBuilder(cookie.Name).Append('=').Append(cookie.Value);

        if (cookie.Expires.HasValue) {
            builder.Append("; Expires=").Append(cookie.Expires.Value.ToUniversalTime().ToString("R"));
        }

        if (cookie.MaxAge.HasValue) {
            builder.Append("; Max-Age=").Append(cookie.MaxAge.Value);
        }

        if (!string.IsNullOrEmpty(cookie.Domain)) {
            builder.Append("; Domain=").Append(cookie.Domain);
        }

        if (!string.IsNullOrEmpty(cookie.Path)) {
            builder.Append("; Path=").Append(cookie.Path);
        }

        switch (cookie.SameSite) {
            case SameSite.Lax:
                builder.Append("; SameSite=Lax");
                break;
            case SameSite.Strict:
                builder.Append("; SameSite=Strict");
                break;
            case SameSite.ExplicitNone:
                builder.Append("; SameSite=None");
                break;
        }

        if (cookie.HttpOnly == true) {
            builder.Append("; HttpOnly");
        }

        if (cookie.Secure == true) {
            builder.Append("; Secure");
        }

        return builder.ToString();
    }
}
