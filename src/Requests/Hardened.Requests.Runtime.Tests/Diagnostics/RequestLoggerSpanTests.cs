using System.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Logging;
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
        string? route = null) {

        var request = Substitute.For<IExecutionRequest>();
        request.Method.Returns(method);
        request.Path.Returns(path);

        var response = Substitute.For<IExecutionResponse>();
        response.Status.Returns(status);

        var context = Substitute.For<IExecutionContext>();
        context.Request.Returns(request);
        context.Response.Returns(response);
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
}
