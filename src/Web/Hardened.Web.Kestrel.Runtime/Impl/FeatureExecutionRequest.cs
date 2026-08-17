using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Shared.Runtime.Collections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Hardened.Requests.Runtime.Headers;

namespace Hardened.Web.Kestrel.Runtime.Impl;

/// <summary>
/// An execution request backed directly by the server's <see cref="IHttpRequestFeature"/>.
///
/// The saving over <c>AspNetExecutionRequest</c> is the absence of an <c>HttpRequest</c> in
/// between. <c>HttpRequest</c> is itself a facade over this same feature, so reading the feature
/// directly removes a layer of indirection on every property — and, more importantly, removes the
/// need to convert ASP.NET's parsed representations back into Hardened's.
/// </summary>
public sealed class FeatureExecutionRequest : IExecutionRequest {
    private readonly IHttpRequestFeature _feature;
    private readonly ITransportInfo _transport;

    // Values supplied by Clone. Null means "read through to the feature", which is what keeps
    // Clone(x: null) preserving the current value.
    private readonly string? _methodOverride;
    private readonly string? _pathOverride;
    private readonly IDictionary<string, StringValues>? _headersOverride;
    private readonly IQueryStringCollection? _queryStringOverride;
    private readonly IReadOnlyList<string>? _cookiesOverride;

    private IQueryStringCollection? _queryString;
    private IReadOnlyList<string>? _cookies;
    private IPathTokenCollection? _pathTokens;

    public FeatureExecutionRequest(IHttpRequestFeature feature)
        : this(feature, null) { }

    /// <param name="connection">
    /// The connection feature, or null when the server did not supply one. Optional rather than
    /// required because a request feature is all a test needs to construct one of these, and the
    /// conformance suite does exactly that.
    /// </param>
    public FeatureExecutionRequest(IHttpRequestFeature feature, IHttpConnectionFeature? connection) {
        _feature = feature;
        _transport = new FeatureTransportInfo(connection, feature);
    }

    private FeatureExecutionRequest(
        IHttpRequestFeature feature,
        string? methodOverride,
        string? pathOverride,
        IDictionary<string, StringValues>? headersOverride,
        IQueryStringCollection? queryStringOverride,
        IReadOnlyList<string>? cookiesOverride,
        ITransportInfo transport) {
        _feature = feature;
        _transport = transport;
        _methodOverride = methodOverride;
        _pathOverride = pathOverride;

        // A fork is handed a plain dictionary, which is very likely case-sensitive, while the
        // request it forked from was reading the feature's own case-insensitive collection. Left
        // alone the override silently changes how header names resolve for the rest of that chain.
        _headersOverride = headersOverride is null
            ? null
            : HeaderCollectionStringValues.EnsureCaseInsensitive(headersOverride);
        _queryStringOverride = queryStringOverride;
        _cookiesOverride = cookiesOverride;
    }

    /// <summary>
    /// A null argument keeps the current value, a non-null argument replaces it.
    /// </summary>
    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null) {
        return new FeatureExecutionRequest(
            _feature,
            method ?? _methodOverride,
            path ?? _pathOverride,
            headers ?? _headersOverride,
            queryString ?? _queryStringOverride,
            cookies ?? _cookiesOverride,
            // Shared, not cloned: a fork is the same request on the same connection, and rebinding
            // its method or path says nothing about where it came from.
            _transport) {
            // Cloned, not shared: a forked chain must be able to rebind without writing through
            // to the request it was forked from. See the conformance suite.
            Parameters = Parameters?.Clone(),
            PathTokens = PathTokens
        };
    }

    public string Method => _methodOverride ?? _feature.Method;

    /// <summary>
    /// The path within the application, matching ASP.NET's <c>HttpRequest.Path</c> semantics.
    ///
    /// <see cref="IHttpRequestFeature.PathBase"/> is deliberately not prepended. PathBase is the
    /// prefix already stripped before the application sees the request, so routes are expected to
    /// match what remains. In this hosting model nothing populates it — path base splitting is
    /// done by ASP.NET's UsePathBase middleware, which is not in play — but honouring the
    /// convention keeps behaviour identical if a host ever does set it.
    /// </summary>
    public string Path => _pathOverride ?? _feature.Path;

    public string? ContentType => Headers.GetOrDefault(KnownHeaders.ContentType);

    public string? Accept => Headers.GetOrDefault(KnownHeaders.Accept);

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body {
        get => _feature.Body;
        set => _feature.Body = value;
    }

    /// <summary>
    /// The feature's header collection, handed over as-is.
    ///
    /// Kestrel's <c>IHeaderDictionary</c> already implements
    /// <c>IDictionary&lt;string, StringValues&gt;</c>, so no adapter is needed and no copy is
    /// made. <c>AspNetExecutionRequest</c> reaches the same object through
    /// <c>HttpRequest.Headers</c>, which indirects back into this feature.
    /// </summary>
    public IDictionary<string, StringValues> Headers => _headersOverride ?? _feature.Headers;

    /// <summary>
    /// Parsed once, lazily, from the raw query string the server captured.
    ///
    /// This is the single largest per-request saving over the ASP.NET adapter, which reads
    /// <c>HttpRequest.Query</c> — already parsed into an <c>IQueryCollection</c> by ASP.NET — and
    /// then materialises a second <c>Dictionary</c> from it with a <c>ToString()</c> per value.
    /// Measured at 165-180 ns per request on routes carrying a query string.
    /// </summary>
    public IQueryStringCollection QueryString =>
        _queryStringOverride ?? (_queryString ??= ParseQueryString(_feature.QueryString));

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    /// <summary>
    /// The framework contract is the raw <c>name=value</c> form that API Gateway delivers, which
    /// is also the wire form, so the Cookie header is split rather than parsed into pairs and
    /// reassembled the way the ASP.NET adapter has to.
    /// </summary>
    public IReadOnlyList<string> Cookies =>
        _cookiesOverride ?? (_cookies ??= ParseCookies(Headers));

    public ITransportInfo Transport => _transport;

    private static IQueryStringCollection ParseQueryString(string? rawQueryString) {
        if (string.IsNullOrEmpty(rawQueryString) || rawQueryString == "?") {
            return EmptyQueryStringCollection.Instance;
        }

        var trimmed = rawQueryString[0] == '?' ? rawQueryString[1..] : rawQueryString;
        var values = new Dictionary<string, string>();

        foreach (var pair in trimmed.Split('&')) {
            if (pair.Length == 0) {
                continue;
            }

            var separator = pair.IndexOf('=');

            if (separator > -1) {
                values[Uri.UnescapeDataString(pair[..separator])] =
                    Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
            else {
                values[Uri.UnescapeDataString(pair)] = "";
            }
        }

        return new SimpleQueryStringCollection(values);
    }

    private static IReadOnlyList<string> ParseCookies(IDictionary<string, StringValues> headers) {
        if (!headers.TryGetValue(KnownHeaders.Cookie, out var cookieHeader)) {
            return Array.Empty<string>();
        }

        var raw = cookieHeader.ToString();

        return string.IsNullOrEmpty(raw)
            ? Array.Empty<string>()
            : raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
