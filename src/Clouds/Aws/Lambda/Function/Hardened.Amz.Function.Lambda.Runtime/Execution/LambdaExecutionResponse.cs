using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
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
            TemplateName = TemplateName,
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

    public string? TemplateName { get; set; }

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