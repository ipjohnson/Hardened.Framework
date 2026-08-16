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

    /// <summary>
    /// Header backed, and lazily, so a cookie is observable to a test the same way it is to a
    /// client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be a <see cref="CookieSetCollectionImpl"/>, which only records: it stores into
    /// a dictionary and something else is expected to serialise it. That is right on API Gateway,
    /// whose proxy response carries a <c>cookies</c> array beside its headers and whose processor
    /// reads the dictionary. Nothing read it here, so <c>Response.Cookies.Append(...)</c> compiled,
    /// ran, and left <c>TestWebResponse.Headers</c> with no <c>Set-Cookie</c> — a cookie that
    /// worked in production could not be asserted at all.
    /// </para>
    /// <para>
    /// Lazy rather than built in the constructor because <see cref="Headers"/> is settable and
    /// <c>Clone</c> may replace it; binding on first use means the cookies land wherever the
    /// headers are now. The same shape the ASP.NET and Kestrel responses use, which is the point —
    /// <c>ExecutionResponseConformanceTests</c> holds all three to it.
    /// </para>
    /// </remarks>
    public ICookieSetCollection Cookies => _cookies ??= new HeaderCookieSetCollection(Headers);

    private ICookieSetCollection? _cookies;

    public bool ShouldSerialize { get; set; } = true;
}