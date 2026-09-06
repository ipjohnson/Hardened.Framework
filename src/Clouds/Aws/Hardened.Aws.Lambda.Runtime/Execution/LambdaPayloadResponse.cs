using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Runtime.Execution;

/// <summary>
/// The payload-shaped response: a payload, nothing, or a batch result.
/// </summary>
/// <remarks>
/// It carries a status and headers even though the runtime sends neither, and that is deliberate
/// rather than vestigial. The pipeline sets a status on refusals and the batch filter reads it back
/// - a record whose fork set 300 or above is a failed record - so the slot is how a payload-shaped
/// answer says it did not succeed. Nothing writes it to the wire.
/// </remarks>
public class LambdaPayloadResponse : IExecutionResponse {
    private readonly IDictionary<string, StringValues> _headers =
        new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

    public LambdaPayloadResponse(Stream body, IHeaderCollection? headers = null) {
        Body = body;
        Headers = headers ?? new HeaderCollectionStringValues();
        Cookies = new CookieSetCollectionImpl();
    }

    public string? ContentType {
        get => Headers.Get(KnownHeaders.ContentType);
        set => Headers.Set(KnownHeaders.ContentType, value);
    }

    public object? ResponseValue { get; set; }

    public IHardenedResponseOutput? Output { get; set; }

    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    public int? Status { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers { get; }

    IDictionary<string, StringValues> IExecutionResponse.Headers => _headers;

    public Exception? ExceptionValue { get; set; }

    /// <summary>
    /// Whether anything has been written. There is no connection to have flushed, so the position
    /// of the body is the whole of it.
    /// </summary>
    public bool ResponseStarted => Body.Position > 0;

    public bool IsBinary { get; set; }

    public ICookieSetCollection Cookies { get; }

    public bool ShouldSerialize { get; set; } = true;

    public object Clone() => Clone(null);

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        return new LambdaPayloadResponse(Body, headerCollection ?? Headers) {
            ResponseValue = ResponseValue,
            OutputFactory = OutputFactory,
            Output = Output,
            Status = Status,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize
        };
    }
}
