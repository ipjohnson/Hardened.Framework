using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;

namespace Hardened.Amz.Function.Lambda.Runtime.Execution;

public class LambdaExecutionRequest : IExecutionRequest
{
    private IPathTokenCollection? _pathTokens;

    public LambdaExecutionRequest(string method, string path, Stream body, IHeaderCollection headers)
    {
        Method = method;
        Path = path;
        Body = body;
        Headers = headers;
    }

    public object Clone()
    {
        throw new NotImplementedException();
    }

    public string Method { get; }

    public string Path { get; }

    public string? ContentType => Headers.Get("Content-Type");

    public string? Accept => Headers.Get("Accept");

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers { get; }

    public IQueryStringCollection QueryString => EmptyQueryStringCollection.Instance;

    public IPathTokenCollection PathTokens
    {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }

    public IReadOnlyList<string> Cookies => Array.Empty<string>();
}