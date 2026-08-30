using System.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Logging;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Primitives;
using Hardened.Shared.Runtime.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Diagnostics;

/// <summary>
/// The span <see cref="RequestLogger"/> produces alongside its log lines.
/// </summary>
/// <remarks>
/// Attribute names are written as literals here rather than shared with the code under test. They
/// are a contract with every trace backend that reads them, and a constant shared between the two
/// would let a rename pass this file while breaking every consumer.
/// </remarks>
[Collection(DiagnosticsListenerCollection.Name)]
public class RequestLoggerSpanTests {

    private static RequestLogger Logger() => new(NullLogger<RequestLogger>.Instance);

    private static IExecutionContext Context(
        string method = "GET",
        string path = "/orders/42",
        int? status = null,
        string? route = null,
        IDictionary<string, StringValues>? headers = null) {

        var request = Substitute.For<IExecutionRequest>();
        request.Method.Returns(method);
        request.Path.Returns(path);

        // A real header collection rather than a substitute, so lookups behave the way they do on a
        // transport - case-insensitively.
        request.Headers.Returns(new HeaderCollectionStringValues(
            headers ?? new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)));

        var response = Substitute.For<IExecutionResponse>();
        response.Status.Returns(status);

        var context = Substitute.For<IExecutionContext>();
        context.Request.Returns(request);
        context.Response.Returns(response);
        context.RequestMetrics.Returns(Substitute.For<IMetricLogger>());
        context.StartTime.Returns(MachineTimestamp.Now);

        if (route != null) {
            var handlerInfo = Substitute.For<IExecutionRequestHandlerInfo>();
            handlerInfo.Path.Returns(route);
            handlerInfo.HandlerType.Returns(typeof(RequestLoggerSpanTests));
            handlerInfo.InvokeMethod.Returns("Handle");

            context.HandlerInfo.Returns(handlerInfo);
        }

        return context;
    }

    /// <summary>
    /// Collects the spans produced while it is alive.
    /// </summary>
    private sealed class Listening : IDisposable {
        private readonly ActivityListener _listener;

        public Listening() {
            _listener = new ActivityListener {
                ShouldListenTo = source => source.Name == "Hardened.Requests",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = Started.Add,
                ActivityStopped = Stopped.Add
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Started { get; } = [];

        public List<Activity> Stopped { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// The instrumentation is unconditional, so the uninstrumented path has to cost nothing. A
    /// begin with no listener produces no span and stores nothing.
    /// </summary>
    [Fact]
    public void NoSpanIsStartedWhenNothingIsListening() {
        Logger().RequestBegin(Context());

        Assert.Null(Activity.Current);
    }

    [Fact]
    public void ABeginStartsAServerSpan() {
        using var listening = new Listening();

        Logger().RequestBegin(Context());

        var span = Assert.Single(listening.Started);

        Assert.Equal(ActivityKind.Server, span.Kind);
    }

    /// <summary>
    /// Named for the method alone at this point: routing has not run, and the conventions are
    /// explicit that a raw path must never become a span name — one span per order id is a trace
    /// backend's worst case.
    /// </summary>
    [Fact]
    public void TheSpanIsNamedForTheMethodBeforeRoutingHasRun() {
        using var listening = new Listening();

        Logger().RequestBegin(Context(method: "GET", path: "/orders/42"));

        Assert.Equal("GET", Assert.Single(listening.Started).DisplayName);
    }

    [Fact]
    public void TheSpanCarriesTheMethodAndPath() {
        using var listening = new Listening();

        Logger().RequestBegin(Context(method: "POST", path: "/orders/42"));

        var span = Assert.Single(listening.Started);

        Assert.Equal("POST", span.GetTagItem("http.request.method"));
        Assert.Equal("/orders/42", span.GetTagItem("url.path"));
    }

    /// <summary>
    /// The route template, not the path that arrived — <c>http.route</c> is the low-cardinality
    /// dimension every latency chart groups by, and <c>/orders/42</c> would make it useless.
    /// </summary>
    [Fact]
    public void MappingAttachesTheRouteTemplateRatherThanThePath() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context(path: "/orders/42", route: "/orders/{id}");

        logger.RequestBegin(context);
        logger.RequestMapped(context);

        Assert.Equal("/orders/{id}", Assert.Single(listening.Started).GetTagItem("http.route"));
    }

    /// <summary>
    /// And the span is renamed the moment routing decides, which is before the handler runs. ASP.NET
    /// has to go back and rename its span at the end of the request instead.
    /// </summary>
    [Fact]
    public void MappingRenamesTheSpanToMethodAndRoute() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context(method: "GET", path: "/orders/42", route: "/orders/{id}");

        logger.RequestBegin(context);
        logger.RequestMapped(context);

        Assert.Equal("GET /orders/{id}", Assert.Single(listening.Started).DisplayName);
    }

    [Fact]
    public void EndStopsTheSpanAndRecordsTheStatusCode() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context(status: 200);

        logger.RequestBegin(context);
        logger.RequestEnd(context);

        var span = Assert.Single(listening.Stopped);

        Assert.Equal(200, span.GetTagItem("http.response.status_code"));
    }

    /// <summary>
    /// Nothing assigns a status on an ordinary success path in this pipeline — null means "handled,
    /// no opinion", and each transport renders it as 200 when it writes the response. Leaving the
    /// tag off for that case would strip a required attribute from every successful span, which is
    /// what an integration test through a real host caught and the unit tests here did not.
    /// </summary>
    [Fact]
    public void ARequestThatSetNoStatusIsRecordedAsTwoHundred() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context(status: null);

        logger.RequestBegin(context);
        logger.RequestEnd(context);

        Assert.Equal(200, Assert.Single(listening.Stopped).GetTagItem("http.response.status_code"));
    }

    /// <summary>
    /// 5xx is the server failing, and shows up in a backend's error rate.
    /// </summary>
    [Fact]
    public void AServerErrorMarksTheSpanErrored() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context(status: 503);

        logger.RequestBegin(context);
        logger.RequestEnd(context);

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(listening.Stopped).Status);
    }

    /// <summary>
    /// 4xx is the caller's mistake and is deliberately left Unset, so that an error rate means "this
    /// service is failing" rather than "someone requested something that is not there".
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    public void AClientErrorLeavesTheSpanUnset(int status) {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context(status: status);

        logger.RequestBegin(context);
        logger.RequestEnd(context);

        Assert.Equal(ActivityStatusCode.Unset, Assert.Single(listening.Stopped).Status);
    }

    [Fact]
    public void AFailureRecordsTheExceptionTypeAndMarksTheSpanErrored() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context();

        logger.RequestBegin(context);
        logger.RequestFailed(context, new InvalidOperationException("handler blew up"));

        var span = Assert.Single(listening.Started);

        Assert.Equal("System.InvalidOperationException", span.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    /// <summary>
    /// The stack goes on the span as well as in the log line. Logs and traces are sampled and
    /// retained separately, so a span that says only "it failed" sends whoever is reading it looking
    /// for a log line that may no longer exist.
    /// </summary>
    [Fact]
    public void AFailureCarriesTheStackOnAnExceptionEvent() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context();

        logger.RequestBegin(context);
        logger.RequestFailed(context, new InvalidOperationException("handler blew up"));

        var recorded = Assert.Single(Assert.Single(listening.Started).Events);

        Assert.Equal("exception", recorded.Name);
        Assert.Equal("System.InvalidOperationException", recorded.Tags.Single(t => t.Key == "exception.type").Value);
        Assert.Equal("handler blew up", recorded.Tags.Single(t => t.Key == "exception.message").Value);
        Assert.Contains("InvalidOperationException", (string)recorded.Tags.Single(t => t.Key == "exception.stacktrace").Value!);
    }

    /// <summary>
    /// A thrown client error leaves the span Unset, which is what <see cref="RequestEnd"/> does with
    /// the same status. Marking it errored put a declared 404 on a trace backend's error rate while
    /// the identical 404 returned rather than thrown did not - so whether an application threw or
    /// returned its response changed what the service looked like.
    /// </summary>
    [Fact]
    public void AThrownClientErrorLeavesTheSpanUnset() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context();

        logger.RequestBegin(context);
        logger.RequestFailed(context, new NotFound("order 42").AsException());

        Assert.Equal(ActivityStatusCode.Unset, Assert.Single(listening.Started).Status);
    }

    /// <summary>
    /// And carries no stack, because the log line for the same failure carries none either. The
    /// throw site of a deliberate throw is the line that threw.
    /// </summary>
    [Fact]
    public void AThrownClientErrorRecordsNoStackOnTheSpan() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context();

        logger.RequestBegin(context);
        logger.RequestFailed(context, new NotFound("order 42").AsException());

        var recorded = Assert.Single(Assert.Single(listening.Started).Events);

        Assert.Equal("exception", recorded.Name);
        Assert.DoesNotContain(recorded.Tags, t => t.Key == "exception.stacktrace");
    }

    /// <summary>
    /// A thrown 503 is a fault whoever threw it, so the span says so. Severity follows the status
    /// rather than whether the exception was recognised.
    /// </summary>
    [Fact]
    public void AThrownServerErrorMarksTheSpanErrored() {
        using var listening = new Listening();

        var logger = Logger();
        var context = Context();

        logger.RequestBegin(context);
        logger.RequestFailed(context, new ServiceUnavailable(Detail: "maintenance").AsException());

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(listening.Started).Status);
    }

    /// <summary>
    /// Each request gets its own span, keyed by its own context. Taking the span from
    /// <c>Activity.Current</c> instead would hand the second request the first one's.
    /// </summary>
    [Fact]
    public void TwoRequestsGetTwoSpans() {
        using var listening = new Listening();

        var logger = Logger();
        var first = Context(path: "/orders/1", status: 200);
        var second = Context(path: "/orders/2", status: 201);

        logger.RequestBegin(first);
        logger.RequestBegin(second);
        logger.RequestEnd(second);
        logger.RequestEnd(first);

        Assert.Equal(2, listening.Stopped.Count);
        Assert.Equal(
            [201, 200],
            listening.Stopped.Select(s => s.GetTagItem("http.response.status_code")));
    }

    // ---- metric dimensions -------------------------------------------------------------------

    /// <summary>
    /// Without these, <c>http.server.request.duration</c> is one histogram for the whole service.
    /// </summary>
    /// <remarks>
    /// Attached whether or not anything is tracing, because metrics and traces are collected
    /// independently — a service exporting metrics and no traces still needs to group by route.
    /// Safe to attach unconditionally because a provider decides what becomes of a tag: the Meter
    /// bridge makes it a histogram dimension, and EMF promotes nothing unless the application opted
    /// the tag in, since a CloudWatch dimension is billed per distinct value.
    /// </remarks>
    [Fact]
    public void TheDimensionsAreTaggedWithNothingListening() {
        var logger = Logger();
        var context = Context(method: "POST", route: "/orders/{id}", status: 201);

        logger.RequestBegin(context);
        logger.RequestMapped(context);
        logger.RequestEnd(context);

        context.RequestMetrics.Received(1).Tag("http.request.method", "POST");
        context.RequestMetrics.Received(1).Tag("http.route", "/orders/{id}");
        context.RequestMetrics.Received(1).Tag("http.response.status_code", 201);
    }

    [Fact]
    public void ARequestThatSetNoStatusIsTaggedAsTwoHundred() {
        var logger = Logger();
        var context = Context(status: null);

        logger.RequestBegin(context);
        logger.RequestEnd(context);

        context.RequestMetrics.Received(1).Tag("http.response.status_code", 200);
    }

    // ---- trace context propagation -----------------------------------------------------------

    private const string CallerTraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string CallerSpanId = "b7ad6b7169203331";
    private const string CallerTraceparent = "00-" + CallerTraceId + "-" + CallerSpanId + "-01";

    private static Dictionary<string, StringValues> Headers(params (string Name, string Value)[] headers) {
        var dictionary = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in headers) {
            dictionary[name] = value;
        }

        return dictionary;
    }

    /// <summary>
    /// The span joins the caller's trace rather than starting a second one that looks unrelated.
    /// </summary>
    [Fact]
    public void ASpanJoinsTheTraceItsCallerSent() {
        using var listening = new Listening();

        Logger().RequestBegin(Context(headers: Headers(("traceparent", CallerTraceparent))));

        var span = Assert.Single(listening.Started);

        Assert.Equal(CallerTraceId, span.TraceId.ToHexString());
        Assert.Equal(CallerSpanId, span.ParentSpanId.ToHexString());
    }

    /// <summary>
    /// Header names are case-insensitive and no two transports spell them alike, so the lookup has to
    /// be too — API Gateway lowercases, and a hand-written client may not.
    /// </summary>
    [Theory]
    [InlineData("traceparent")]
    [InlineData("Traceparent")]
    [InlineData("TRACEPARENT")]
    public void TheTraceparentIsFoundUnderAnySpelling(string spelling) {
        using var listening = new Listening();

        Logger().RequestBegin(Context(headers: Headers((spelling, CallerTraceparent))));

        Assert.Equal(CallerTraceId, Assert.Single(listening.Started).TraceId.ToHexString());
    }

    [Fact]
    public void TracestateRidesAlongUnparsed() {
        using var listening = new Listening();

        Logger().RequestBegin(Context(headers: Headers(
            ("traceparent", CallerTraceparent),
            ("tracestate", "vendor=opaque-value"))));

        Assert.Equal("vendor=opaque-value", Assert.Single(listening.Started).TraceStateString);
    }

    /// <summary>
    /// A caller that sends nothing gets a root span, which is the ordinary case for a public
    /// endpoint.
    /// </summary>
    [Fact]
    public void NoTraceparentStartsANewTrace() {
        using var listening = new Listening();

        Logger().RequestBegin(Context());

        var span = Assert.Single(listening.Started);

        Assert.Equal(default(ActivitySpanId).ToHexString(), span.ParentSpanId.ToHexString());
    }

    /// <summary>
    /// And so does a caller that sends nonsense. ActivityContext.TryParse validates rather than
    /// trusts, so a span never claims a parent it does not have — anything else would attach this
    /// request to a trace that does not exist, or to somebody else's.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c")]
    [InlineData("00-00000000000000000000000000000000-b7ad6b7169203331-01")]
    [InlineData("ff-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01")]
    public void AMalformedTraceparentStartsANewTrace(string traceparent) {
        using var listening = new Listening();

        Logger().RequestBegin(Context(headers: Headers(("traceparent", traceparent))));

        var span = Assert.Single(listening.Started);

        Assert.Equal(default(ActivitySpanId).ToHexString(), span.ParentSpanId.ToHexString());
        Assert.NotEqual(CallerTraceId, span.TraceId.ToHexString());
    }

    /// <summary>
    /// A version this code has never heard of is still honoured. W3C trace context is deliberately
    /// forward-compatible — an implementation that meets a higher version parses the fields it knows
    /// rather than discarding the trace — and only <c>ff</c> is reserved as invalid. Worth an
    /// assertion because the obvious reading is that an unknown version is malformed, and dropping
    /// the parent would quietly break tracing against any future caller.
    /// </summary>
    [Fact]
    public void AFutureTraceContextVersionIsStillHonoured() {
        using var listening = new Listening();

        Logger().RequestBegin(Context(headers: Headers(
            ("traceparent", "99-" + CallerTraceId + "-" + CallerSpanId + "-01"))));

        var span = Assert.Single(listening.Started);

        Assert.Equal(CallerTraceId, span.TraceId.ToHexString());
        Assert.Equal(CallerSpanId, span.ParentSpanId.ToHexString());
    }
}
