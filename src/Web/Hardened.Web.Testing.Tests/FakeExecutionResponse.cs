using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing.Tests;

/// <summary>
/// Minimal <see cref="IExecutionResponse"/> stand-in for exercising
/// <see cref="TestWebResponse"/> and <see cref="WebAssertThat"/> directly, without
/// standing up a web application.
/// </summary>
internal class FakeExecutionResponse : IExecutionResponse {
    public FakeExecutionResponse(int? status = null, Stream? body = null) {
        Status = status;
        Body = body ?? new MemoryStream();
    }

    public int? Status { get; set; }

    public Stream Body { get; set; }

    public IDictionary<string, StringValues> Headers { get; } = new Dictionary<string, StringValues>();

    public string? ContentType { get; set; }

    public object? ResponseValue { get; set; }

    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    public IHardenedResponseOutput? Output { get; set; }

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => false;

    public bool IsBinary { get; set; }

    public bool ShouldSerialize { get; set; }

    // Not exercised by these tests; kept unimplemented so an accidental dependency
    // on them surfaces loudly rather than silently returning a default.
    public IExecutionResponse Clone(IHeaderCollection? headerCollection = null) =>
        throw new NotSupportedException();

    public ICookieSetCollection Cookies => throw new NotSupportedException();
}
