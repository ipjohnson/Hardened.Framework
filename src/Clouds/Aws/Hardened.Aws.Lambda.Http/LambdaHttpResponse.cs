using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Http;

/// <summary>
/// The web-shaped response. Accumulates a status, headers, cookies and a body; the adapter turns
/// that into the payload format 2.0 response once the chain has finished.
/// </summary>
/// <remarks>
/// <para>
/// <b>It holds no proxy response.</b> The old implementation wrote through to one as the chain ran,
/// and that arrangement cost this transport every 404 it should have sent:
/// <c>APIGatewayHttpApiV2ProxyResponse.StatusCode</c> is a non-nullable <c>int</c> starting at zero,
/// so reading the status back could never produce null, and <c>ResourceNotFoundHandler</c> supplies
/// a 404 only when it finds the status still unset. It never fired. An unmatched route left the
/// status at zero, which the host normalised to 200, so every path the routing table did not match
/// came back as an empty 200 - while the streaming transport, which backed its status with its own
/// field, returned 404 for the same application.
/// </para>
/// <para>
/// Separating the two is what makes that unrepresentable rather than fixed: there is no proxy
/// response in existence until <c>WriteResponse</c> builds one, so nothing can read a status out of
/// a field that has no null.
/// </para>
/// </remarks>
public class LambdaHttpResponse : IExecutionResponse {
    private IHeaderCollection? _headerCollection;

    public LambdaHttpResponse(Stream body) {
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
        var clone = new LambdaHttpResponse(Body) {
            ResponseValue = ResponseValue,
            OutputFactory = OutputFactory,
            Output = Output,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
            // Copied rather than shared: a clone starts where the original stands and diverges from
            // there.
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
