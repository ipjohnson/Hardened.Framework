using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Http;

/// <summary>
/// The web-shaped response. Accumulates a status, headers, cookies and a body; the adapter turns
/// that into the worker's response data once the chain has finished.
/// </summary>
/// <remarks>
/// It holds no <c>HttpResponseData</c> while the chain runs, for the reason
/// <c>LambdaHttpResponse</c> gives: the status has to be null until something sets it, so the
/// not-found handler can tell an unmatched route from an answered one, and the worker's status is
/// an enum with no null.
/// </remarks>
public class HttpFunctionResponse : IExecutionResponse {
    private IHeaderCollection? _headerCollection;

    public HttpFunctionResponse(Stream body) {
        Body = body;
        Cookies = new CookieSetCollectionImpl();
    }

    public string? ContentType {
        get => Headers.Get(KnownHeaders.ContentType);
        set => Headers.Set(KnownHeaders.ContentType, value);
    }

    public object? ResponseValue { get; set; }

    public IHardenedResponseOutput? Output { get; set; }

    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    /// <summary>Null while the status is still undecided; otherwise what will be sent.</summary>
    public int? Status { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers => _headerCollection ??= new HeaderCollectionStringValues();

    IDictionary<string, StringValues> IExecutionResponse.Headers => Headers;

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => Body.Position > 0;

    public bool IsBinary { get; set; }

    public ICookieSetCollection Cookies { get; }

    public bool ShouldSerialize { get; set; } = true;

    public object Clone() => Clone(null);

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        var clone = new HttpFunctionResponse(Body) {
            ResponseValue = ResponseValue,
            OutputFactory = OutputFactory,
            Output = Output,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
            Status = Status
        };

        if (headerCollection != null) {
            foreach (var header in headerCollection) {
                clone.Headers.Set(header.Key, header.Value);
            }
        }

        return clone;
    }
}
