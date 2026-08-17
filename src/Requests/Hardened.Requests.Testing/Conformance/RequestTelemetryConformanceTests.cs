using System.Diagnostics;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// The telemetry every host must produce, expressed once and executed against each of them.
/// </summary>
/// <remarks>
/// <para>
/// The third contract, beside <see cref="ExecutionRequestConformanceTests"/> and
/// <see cref="ExecutionResponseConformanceTests"/>. Those two test an object; this one tests that a
/// host calls it. The distinction is the whole point: the two Lambda streaming engines ran requests
/// and told <c>IRequestLogger</c> nothing whatsoever, so they produced no request logging and would
/// have produced no spans. Every object involved was correct — nothing invoked them — and no
/// object-level suite could have noticed.
/// </para>
/// <para>
/// Attribute names are literals rather than constants shared with the code under test. They are the
/// contract a trace backend reads, so a rename has to fail here rather than pass.
/// </para>
/// <para>
/// To enrol a host, derive from this class and supply an adapter:
/// </para>
/// <code>
/// public class MyHostTelemetryConformanceTests : RequestTelemetryConformanceTests {
///     protected override IRequestTelemetryConformanceAdapter Adapter { get; } = new MyAdapter();
/// }
/// </code>
/// </summary>
public abstract class RequestTelemetryConformanceTests {
    private static int _discriminator;

    protected abstract IRequestTelemetryConformanceAdapter Adapter { get; }

    private string Because(string what) => $"[{Adapter.TransportName}] {what}";

    /// <summary>
    /// Dispatches one request and returns the span it produced.
    /// </summary>
    /// <remarks>
    /// An <c>ActivitySource</c> is process-global and the rest of a host's test assembly runs in
    /// parallel through the same one, so the span is picked out inside the callback by a path no other
    /// request uses. Collecting everything and filtering afterwards writes a shared list while
    /// reading it, which fails intermittently rather than honestly.
    /// </remarks>
    private async Task<Activity> Dispatch(
        string method = "GET",
        Action<Hardened.Requests.Abstract.Execution.IExecutionContext>? handler = null,
        params (string Name, string Value)[] headers) {

        var path = "/conformance/telemetry/" + Interlocked.Increment(ref _discriminator);

        Activity? captured = null;

        using var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == "Hardened.Requests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = span => {
                if ((string?)span.GetTagItem("url.path") == path) {
                    captured = span;
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        var headerCollection = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in headers) {
            headerCollection[name] = value;
        }

        await Adapter.Dispatch(new TelemetryConformanceRequest {
            Method = method,
            Path = path,
            Headers = headerCollection,
            Handler = handler
        });

        Assert.True(captured is not null,
            Because("a request produced no server span that was started and stopped — the host has to " +
                    "report both a beginning and an end"));

        return captured!;
    }

    [Fact]
    public async Task ARequestProducesAServerSpan() {
        var span = await Dispatch();

        Assert.Equal(ActivityKind.Server, span.Kind);
    }

    [Fact]
    public async Task TheSpanCarriesTheMethod() {
        var span = await Dispatch(method: "POST");

        Assert.Equal("POST", span.GetTagItem("http.request.method"));
    }

    /// <summary>
    /// Stopped, not merely started. A span that is never stopped is never exported.
    /// </summary>
    [Fact]
    public async Task TheSpanIsStopped() {
        var span = await Dispatch();

        Assert.NotEqual(TimeSpan.Zero, span.Duration);
    }

    [Fact]
    public async Task AStatusSetByTheHandlerIsRecorded() {
        var span = await Dispatch(handler: context => context.Response.Status = 201);

        Assert.Equal(201, span.GetTagItem("http.response.status_code"));
    }

    /// <summary>
    /// Nothing assigns a status on an ordinary success path — null means "handled, no opinion", and
    /// each transport renders it as 200 when it writes the response. A span that omitted the attribute
    /// for that case would omit it for almost every successful request.
    /// </summary>
    [Fact]
    public async Task ASuccessThatSetNoStatusIsRecordedAsTwoHundred() {
        var span = await Dispatch();

        Assert.Equal(200, span.GetTagItem("http.response.status_code"));
    }

    /// <summary>
    /// 5xx is the server failing and belongs in a backend's error rate.
    /// </summary>
    [Fact]
    public async Task AServerErrorMarksTheSpanErrored() {
        var span = await Dispatch(handler: context => context.Response.Status = 503);

        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    /// <summary>
    /// 4xx is the caller's mistake and deliberately is not, so that an error rate means "this service
    /// is failing" rather than "someone asked for something that is not there".
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    public async Task AClientErrorLeavesTheSpanUnset(int status) {
        var span = await Dispatch(handler: context => context.Response.Status = status);

        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    /// <summary>
    /// The span joins the caller's trace rather than starting a second one that looks unrelated.
    /// </summary>
    [Fact]
    public async Task TheSpanJoinsTheTraceItsCallerSent() {
        const string traceId = "0af7651916cd43dd8448eb211c80319c";
        const string spanId = "b7ad6b7169203331";

        var span = await Dispatch(
            headers: ("traceparent", $"00-{traceId}-{spanId}-01"));

        Assert.Equal(traceId, span.TraceId.ToHexString());
        Assert.Equal(spanId, span.ParentSpanId.ToHexString());
    }

    /// <summary>
    /// And finds it whatever case it arrived in. API Gateway lowercases every header name it
    /// delivers; Kestrel passes through whatever the client sent.
    /// </summary>
    [Fact]
    public async Task TheTraceparentIsFoundWhateverCaseItArrivedIn() {
        const string traceId = "0af7651916cd43dd8448eb211c80319c";

        var span = await Dispatch(
            headers: ("TraceParent", $"00-{traceId}-b7ad6b7169203331-01"));

        Assert.Equal(traceId, span.TraceId.ToHexString());
    }

    /// <summary>
    /// A caller that sends nothing gets a root span, which is the ordinary case for a public
    /// endpoint.
    /// </summary>
    [Fact]
    public async Task ARequestWithNoTraceparentStartsANewTrace() {
        var span = await Dispatch();

        Assert.Equal(default(ActivitySpanId).ToHexString(), span.ParentSpanId.ToHexString());
    }
}
