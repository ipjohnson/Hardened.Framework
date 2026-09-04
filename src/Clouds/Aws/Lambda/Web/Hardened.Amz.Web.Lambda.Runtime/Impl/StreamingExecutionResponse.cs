using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

/// <summary>
/// The response in stream mode: the same contract as <see cref="ApiGatewayV2ExecutionResponse"/>
/// over a <see cref="ResponseStream"/> instead of a buffer.
/// </summary>
internal sealed class StreamingExecutionResponse : IExecutionResponse {
    private readonly ResponseStream _stream;
    private HeaderCollectionStringValues? _headerCollection;

    public StreamingExecutionResponse(ResponseStream stream) {
        _stream = stream;
        Body = stream;
        Cookies = new CookieSetCollectionImpl();
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection = null) {
        return new StreamingExecutionResponse(_stream) {
            ResponseValue = ResponseValue,
            OutputFactory = OutputFactory,
            Output = Output,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
            // Carried, as the buffered response does. A fork whose Body did not come across would
            // write into a stream nobody pumps, and the bytes would vanish silently.
            Body = Body,
            Status = Status,
        };
    }

    public string? ContentType {
        get => Headers.Get(KnownHeaders.ContentType);
        set => Headers.Set(KnownHeaders.ContentType, value);
    }

    public object? ResponseValue { get; set; }

    public IHardenedResponseOutput? Output { get; set; }

    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    public int? Status { get; set; }

    /// <summary>
    /// The stream the pipeline writes to. A filter may wrap it, which is why
    /// <see cref="ResponseStarted"/> reads the stream it was built over rather than this property.
    /// </summary>
    public Stream Body { get; set; }

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringValues();

    IDictionary<string, StringValues> IExecutionResponse.Headers => Headers;

    public Exception? ExceptionValue { get; set; }

    /// <summary>
    /// True once the Lambda response stream is open, which is when the prelude went out and the
    /// status and headers stopped being changeable. What the retry filter and the compressing
    /// stream consult.
    /// </summary>
    public bool ResponseStarted => _stream.HasResponseStarted;

    public bool IsBinary { get; set; }

    public ICookieSetCollection Cookies { get; }

    public bool ShouldSerialize { get; set; } = true;
}
