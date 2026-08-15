using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Testing;

public class TestExecutionResponse : IExecutionResponse {
    public TestExecutionResponse(Stream body) {
        Body = body;
        Cookies = new CookieSetCollectionImpl();
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        return new TestExecutionResponse(Body) {
            ResponseValue = ResponseValue,
            OutputFactory = OutputFactory,
            Output = Output,
            Status = Status,
            ShouldCompress = ShouldCompress,
            Headers = headerCollection as IDictionary<string, StringValues> ?? Headers,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
        };
    }

    public string? ContentType {
        get => Headers.GetOrDefault("Content-Type");
        set => Headers["Content-Type"] = value;
    }

    public object? ResponseValue { get; set; }

    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    public IHardenedResponseOutput? Output { get; set; }

    public int? Status { get; set; }

    public bool ShouldCompress { get; set; }

    public Stream Body { get; set; }

    public IDictionary<string, StringValues> Headers { get; set; } = new Dictionary<string, StringValues>();
    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => Body.Position > 0;

    public bool IsBinary { get; set; }

    public ICookieSetCollection Cookies { get; }
    public bool ShouldSerialize { get; set; } = true;
}