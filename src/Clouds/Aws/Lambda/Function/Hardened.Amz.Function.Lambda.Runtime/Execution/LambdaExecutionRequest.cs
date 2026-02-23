using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Function.Lambda.Runtime.Execution;

public class LambdaExecutionRequest : IExecutionRequest {
    private IPathTokenCollection? _pathTokens;

    public LambdaExecutionRequest(
        string method, 
        string path, 
        Stream body, 
        IDictionary<string, StringValues> headers) {
        Method = method;
        Path = path;
        Body = body;
        Headers = headers;
    }


    public IExecutionRequest Clone(
        string? method,
        string? path,
        IDictionary<string, StringValues> headers,
        IQueryStringCollection queryString,
        IReadOnlyList<string> cookies) {
        return new LambdaExecutionRequest(method ?? this.Method, path ?? this.Path, this.Body,
            headers);
    }

    public string Method { get; }

    public string Path { get; }

    public string? ContentType => Headers.GetOrDefault("Content-Type");

    public string? Accept => Headers.GetOrDefault("Accept");

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IDictionary<string, StringValues> Headers { get; }

    public IQueryStringCollection QueryString => EmptyQueryStringCollection.Instance;

    public IPathTokenCollection PathTokens {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    public IReadOnlyList<string> Cookies => Array.Empty<string>();
}