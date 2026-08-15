using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Function.Lambda.Runtime.Execution;

public class LambdaExecutionResponse : IExecutionResponse {
    private IDictionary<string, StringValues> _headers = new Dictionary<string, StringValues>();

    public LambdaExecutionResponse(Stream body, IHeaderCollection headers) {
        Body = body;
        Headers = headers;
        Cookies = new CookieSetCollectionImpl();
    }

    public object Clone() {
        return Clone(null);
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        return new LambdaExecutionResponse(Body, headerCollection ?? Headers) {
            ResponseValue = ResponseValue,
            OutputFactory = OutputFactory,
            Output = Output,
            Status = Status,
            ShouldCompress = ShouldCompress,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
        };
    }

    public string? ContentType {
        get => Headers.Get(KnownHeaders.ContentType);
        set => Headers.Set(KnownHeaders.ContentType, value);
    }

    public object? ResponseValue { get; set; }

    /// <summary>
    /// Built from <see cref="OutputFactory"/> on first use and kept. Replaces <c>TemplateName</c>,
    /// which named a view by string and is gone - a view is a type now.
    /// </summary>
    public IHardenedResponseOutput? Output { get; set; }

    /// <summary>Builds what writes this response, or null when it is serialized like any other.</summary>
    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    public int? Status { get; set; }

    public bool ShouldCompress { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers { get; }

    IDictionary<string, StringValues> IExecutionResponse.Headers => _headers;

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => Body.Position > 0;

    public bool IsBinary { get; set; }

    public ICookieSetCollection Cookies { get; }

    public bool ShouldSerialize { get; set; } = true;
}