using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Testing;

public class TestExecutionRequest : IExecutionRequest {
    private IPathTokenCollection? _pathTokens;

    public TestExecutionRequest(
        string method,
        string path,
        string? accepts, IQueryStringCollection queryString) {
        Method = method;
        Path = path;
        Accept = accepts;
        QueryString = queryString;
    }

    public IExecutionRequest Clone(
        string? method,
        string? path,
        IDictionary<string, StringValues>? headers,
        IQueryStringCollection? queryString,
        IReadOnlyList<string>? cookies) {
        return new TestExecutionRequest(
            method ?? Method,
            path ?? Path,
            Accept,
            queryString ?? QueryString) {
            // Cloned, not shared: a forked chain must be able to rebind without writing
            // through to the request it was forked from. See the conformance suite.
            Parameters = Parameters?.Clone(),
            Body = Body,
            Headers = headers ?? Headers,
            PathTokens = PathTokens,
            Cookies = cookies ?? Cookies,
            // Shared with the fork rather than reset: a forked chain is the same request on the
            // same connection, which is what the conformance suite asserts.
            Transport = Transport,
        };
    }

    public string Method { get; }

    public string Path { get; }

    public string? ContentType => Headers.GetOrDefault("Content-Type");

    public string? Accept { get; }

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; } = Stream.Null;


    public IDictionary<string, StringValues> Headers { get; set; } = new Dictionary<string, StringValues>();

    public IQueryStringCollection QueryString { get; }

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    public IReadOnlyList<string> Cookies { get; set; } = Array.Empty<string>();

    /// <summary>
    /// What a test says the transport knows, defaulting to nothing.
    /// </summary>
    /// <remarks>
    /// Settable, because that is what makes a test of a forwarded-headers filter or of an
    /// address-partitioned rate limiter possible without a socket. Empty by default rather than
    /// null, so a test that does not care about the transport never has to set it - which is nearly
    /// all of them.
    /// </remarks>
    public ITransportInfo Transport { get; set; } = EmptyTransportInfo.Instance;
}

/// <summary>
/// Transport facts a test states outright.
/// </summary>
/// <remarks>
/// A dictionary rather than the lazy lookup the real transports use, because a test knows every
/// answer up front and there is no connection to avoid touching.
/// </remarks>
public class TestTransportInfo : ITransportInfo {
    private readonly IDictionary<string, string> _values;

    public TestTransportInfo(IDictionary<string, string> values) {
        _values = values;
    }

    public TestTransportInfo(params (string Key, string Value)[] values)
        : this(values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)) { }

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public IReadOnlyList<string> Keys => _values.Keys.ToArray();
}