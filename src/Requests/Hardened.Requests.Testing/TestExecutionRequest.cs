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
        IDictionary<string, StringValues> headers,
        IQueryStringCollection queryString,
        IReadOnlyList<string> cookies) {
        return new TestExecutionRequest(
            method ?? Method,
            path ?? Path,
            Accept,
            queryString ?? QueryString) {
            Parameters = Parameters,
            Body = Body,
            Headers = headers ?? Headers,
            PathTokens = PathTokens,
            Cookies = cookies ?? Cookies,
        };
    }

    public string Method { get; }

    public string Path { get; }

    public string? ContentType => Headers.GetOrDefault("Content-Type");

    public string? Accept { get; }

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }


    public IDictionary<string, StringValues> Headers { get; set; } = new Dictionary<string, StringValues>();

    public IQueryStringCollection QueryString { get; }

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    public IReadOnlyList<string> Cookies { get; set; } = Array.Empty<string>();
}