using System.Diagnostics;
using System.Runtime.CompilerServices;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Logging;

/// <summary>
/// Reports the request lifecycle: a log line, and a span.
/// </summary>
/// <remarks>
/// <para>
/// Both, from one type, because they are the same events. <see cref="IRequestLogger"/> is already a
/// bracketed lifecycle - begin, mapped, end, failed - and <see cref="RequestEnd"/> already computed
/// an elapsed duration before any of this existed. A span is that same record written to a
/// different kind of sink, so it needs no second interface, no scope object to thread through the
/// hosts, and no change at any of the five call sites.
/// </para>
/// <para>
/// Nothing here references OpenTelemetry. <c>ActivitySource</c> is in the base class library and an
/// OTel SDK is one of several things that can subscribe to it - see
/// <see cref="HardenedDiagnostics"/>.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public partial class RequestLogger : IRequestLogger {
    /// <summary>
    /// The span in flight, keyed by the context it belongs to.
    /// </summary>
    /// <remarks>
    /// Keyed rather than taken from <c>Activity.Current</c>, which is ambient and would hand
    /// <see cref="RequestEnd"/> whatever the handler happened to leave current - a child span it
    /// forgot to stop, on the wrong request entirely once a batch forks. The context is the thing
    /// the span actually belongs to, and it is already passed to every method here.
    ///
    /// Weak keys, so a context that is dropped without an end - a host that throws between begin and
    /// end - takes its entry with it rather than pinning the request forever. Empty whenever nothing
    /// is listening, because a null activity is never stored, which is what keeps the lookups on the
    /// uninstrumented path down to a miss on an empty table.
    /// </remarks>
    private static readonly ConditionalWeakTable<IExecutionContext, Activity> _spans = new();

    /// <summary>
    /// The logging scope carrying this request's correlation id, keyed the same way as the span.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scope rather than a parameter on each message, because the point is the lines this class
    /// does <em>not</em> write: a handler logging "charging card" should carry the id without
    /// knowing there is one. Every provider that renders scopes picks it up - Serilog and the
    /// OpenTelemetry logger natively, the console with <c>IncludeScopes = true</c>.
    /// </para>
    /// <para>
    /// The other half of this arrives for free and is worth knowing about: <c>StartActivity</c> sets
    /// <c>Activity.Current</c>, so a trace-aware provider already stamps TraceId and SpanId on every
    /// line without a scope at all. The scope is what covers the case that one does not - no
    /// collector attached, so no span, which is exactly when there is no trace id to stamp.
    /// </para>
    /// </remarks>
    private static readonly ConditionalWeakTable<IExecutionContext, IDisposable> _scopes = new();

    private readonly ILogger<RequestLogger> _logger;

    public RequestLogger(ILogger<RequestLogger> logger) {
        _logger = logger;
    }

    public void RequestBegin(IExecutionContext context) {
        // The span is started before anything else here, and the order is load-bearing rather than
        // stylistic. Starting it sets Activity.Current, and the context's correlation id reads from
        // there on first access - so opening the scope first would realize an id with no trace to
        // read, and the span created a moment later would carry a different one. A log line and a
        // span for the same request would then disagree, which is the one thing this is for.
        //
        // Null when nothing is listening - no allocation, no work - which is why this is
        // unconditional and lives in the runtime rather than behind a flag or an opt-in package.
        //
        // Named for the method alone: the route is not known yet, and the semantic conventions are
        // explicit that a raw path must not become a span name. RequestMapped renames it below.
        //
        // Parented on the caller's traceparent when there is one, so this span joins their trace
        // rather than starting a second one that looks unrelated.
        var span = TryGetParent(context.Request, out var parent)
            ? HardenedDiagnostics.ActivitySource.StartActivity(
                context.Request.Method, ActivityKind.Server, parent)
            : HardenedDiagnostics.ActivitySource.StartActivity(
                context.Request.Method, ActivityKind.Server);

        if (span is not null) {
            span.SetTag("http.request.method", context.Request.Method);
            span.SetTag("url.path", context.Request.Path);

            _spans.AddOrUpdate(context, span);
        }

        // After the span, so the id is that span's trace id when there is one; before the first
        // line, so the framework's own lifecycle messages are not the only ones without it.
        OpenCorrelationScope(context);

        LogRequestStarted(context.Request.Method, context.Request.Path);

        // A dimension for whatever provider is behind IMetricLogger, independent of tracing. What
        // becomes of it is the provider's decision: the Meter bridge makes it a histogram dimension,
        // and EMF promotes nothing unless the application opted the tag in, because a CloudWatch
        // dimension is billed per distinct value.
        context.RequestMetrics.Tag("http.request.method", context.Request.Method);
    }

    /// <summary>
    /// Puts this request's correlation id on every log line written while it runs.
    /// </summary>
    /// <remarks>
    /// Read from the context rather than from <c>Activity.Current</c>, because the context is what
    /// guarantees an id exists at all: with no collector attached there is no span, and the context
    /// generates one of the same shape instead.
    /// </remarks>
    private void OpenCorrelationScope(IExecutionContext context) {
        var correlationId = context.CorrelationId;

        // Substituted contexts in a test hand back null for a property they were never set up with.
        // A scope keyed to nothing is worse than no scope.
        if (string.IsNullOrEmpty(correlationId)) {
            return;
        }

        var scope = _logger.BeginScope(new CorrelationScope(correlationId));

        if (scope is not null) {
            _scopes.AddOrUpdate(context, scope);
        }
    }

    /// <summary>
    /// Closes the scope opened at the start of the request.
    /// </summary>
    /// <remarks>
    /// Every host calls <c>RequestEnd</c> from a <c>finally</c>, so a request that threw still
    /// closes its scope. Leaking one matters more than leaking a span: the scope stacks are
    /// <c>AsyncLocal</c> in every provider that implements them, and an undisposed scope can leave
    /// the id attached to whatever the thread does next.
    /// </remarks>
    private void CloseCorrelationScope(IExecutionContext context) {
        if (!_scopes.TryGetValue(context, out var scope)) {
            return;
        }

        _scopes.Remove(context);

        scope.Dispose();
    }

    /// <summary>
    /// One key and one value, shaped the way structured providers expect a scope to be.
    /// </summary>
    /// <remarks>
    /// <c>IReadOnlyList&lt;KeyValuePair&lt;string, object&gt;&gt;</c> is what Serilog, the
    /// OpenTelemetry logger and the console's <c>IncludeScopes</c> all look for; a provider that
    /// finds anything else falls back to <c>ToString</c>, which is why that is overridden rather
    /// than left to print a type name.
    /// </remarks>
    private sealed class CorrelationScope : IReadOnlyList<KeyValuePair<string, object>> {
        public const string Key = "CorrelationId";

        private readonly string _correlationId;

        public CorrelationScope(string correlationId) {
            _correlationId = correlationId;
        }

        public int Count => 1;

        public KeyValuePair<string, object> this[int index] =>
            index == 0
                ? new KeyValuePair<string, object>(Key, _correlationId)
                : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() {
            yield return new KeyValuePair<string, object>(Key, _correlationId);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public override string ToString() => Key + ":" + _correlationId;
    }

    /// <summary>
    /// The W3C trace context the caller sent, if it sent a usable one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extraction only. Hardened issues no outbound requests, so there is nothing here to inject a
    /// context into - which is the whole of the propagation story on this side.
    /// </para>
    /// <para>
    /// ActivityContext.TryParse does the parsing and the validating, so a malformed traceparent - a
    /// wrong version, a zero trace id, a truncated field - is rejected rather than turned into a span
    /// that claims a parent it does not have. A caller that sends nothing, or sends nonsense, gets a
    /// root span.
    /// </para>
    /// </remarks>
    private static bool TryGetParent(IExecutionRequest request, out ActivityContext parent) {
        parent = default;

        if (!request.Headers.TryGetValue("traceparent", out var traceparent)) {
            return false;
        }

        var value = traceparent.ToString();

        if (string.IsNullOrEmpty(value)) {
            return false;
        }

        // tracestate is vendor data that rides along unparsed. Absent is normal and not a failure.
        request.Headers.TryGetValue("tracestate", out var tracestate);

        return ActivityContext.TryParse(value, tracestate.ToString(), out parent);
    }

    public void RequestMapped(IExecutionContext context) {
        LogRequestMapped(context.Request.Method, context.Request.Path, context.HandlerInfo!.HandlerType.Name,
             context.HandlerInfo!.InvokeMethod);

        // The low-cardinality template, not the path that arrived. Attached here, before the handler
        // runs, because this is the moment routing decided - where ASP.NET has to go back and rename
        // its span after the fact.
        var route = context.HandlerInfo!.Path;

        // Outside the span check: a metric dimension is wanted whether or not anything is tracing,
        // and without it http.server.request.duration is one histogram for the whole service.
        context.RequestMetrics.Tag("http.route", route);

        if (!_spans.TryGetValue(context, out var span)) {
            return;
        }

        span.SetTag("http.route", route);
        span.DisplayName = context.Request.Method + " " + route;
    }

    public void RequestEnd(IExecutionContext context) {
        LogRequestFinished(
            context.Request.Method,
            context.Request.Path,
            context.Response.Status,
            context.StartTime.GetElapsedTime()
        );

        // Null means "handled, no opinion": nothing assigns a status on an ordinary success path,
        // and every transport renders that as 200 when it writes the response. Leaving the tag off
        // instead would strip a required attribute from every successful span, which is what an
        // integration test through the real host caught and the unit tests around this could not.
        //
        // It is only ever null when no one supplied one. The web responses read through -
        // `_status ?? (HasStarted ? StatusCode : null)` - so by the time the request ends this is
        // already the real code, including one that ASP.NET's own pipeline produced after Hardened
        // declined the request.
        var status = context.Response.Status ?? 200;

        // Tagged whether or not anything is tracing. Buffered by the Meter provider until the logger
        // is disposed, which is what lets a dimension be attached after the measurement it describes -
        // every host records TotalRequestDuration and only then calls this.
        context.RequestMetrics.Tag("http.response.status_code", status);

        if (_spans.TryGetValue(context, out var span)) {
            _spans.Remove(context);

            span.SetTag("http.response.status_code", status);

            // 4xx is the caller's mistake, not the server's. The conventions leave a server span
            // Unset for those and reserve Error for 5xx, so that a trace backend's error rate means
            // "this service failed" rather than "someone sent a bad request".
            if (status >= 500) {
                span.SetStatus(ActivityStatusCode.Error);
            }

            span.Stop();
        }

        // Last, so every line above is still written inside it - and outside the span check, which
        // used to return early here. A request with no span is the uninstrumented path, and that is
        // the one whose scope most needs closing, because it is the one that has one.
        CloseCorrelationScope(context);
    }

    public void RequestParameterBindFailed(IExecutionContext context, Exception? exp) {
        _logger.LogError(exp, "{method} {path} failed to bind parameters",
            context.Request.Method, context.Request.Path);
    }

    public void RequestFailed(IExecutionContext context, Exception exp) {
        _logger.LogError(exp, "{method} {path} request failed", context.Request.Method, context.Request.Path);

        if (!_spans.TryGetValue(context, out var span)) {
            return;
        }

        span.SetTag("error.type", exp.GetType().FullName);
        span.SetStatus(ActivityStatusCode.Error, exp.Message);

        // The stack repeats what the log line above already carries, deliberately: logs and traces
        // are sampled and retained separately, and a trace that says only "it failed" sends whoever
        // is reading it looking for a log line that may no longer exist.
        span.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection {
            { "exception.type", exp.GetType().FullName },
            { "exception.message", exp.Message },
            { "exception.stacktrace", exp.ToString() }
        }));
    }

    public void ResourceNotFound(IExecutionContext context) {
        LogResourceNotFound(context.Request.Method, context.Request.Path);
    }

    [LoggerMessage(
        EventId = 78000,
        Level = LogLevel.Information,
        Message = "{httpMethod} {path} started")]
    protected partial void LogRequestStarted(string httpMethod, string path);

    [LoggerMessage(
        EventId = 78001,
        Level = LogLevel.Information,
        Message = "{httpMethod} {path} mapped to {typeName}.{methodName}")]
    protected partial void LogRequestMapped(string httpMethod, string path, string typeName, string methodName);

    [LoggerMessage(
        EventId = 78002,
        Level = LogLevel.Information,
        Message = "{httpMethod} {path}  finished status code '{statusCode}'  duration {durationMs}")]
    protected partial void LogRequestFinished(
        string httpMethod, string path, int? statusCode, TimeSpan durationMs);

    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 78003,
        Level = LogLevel.Information,
        Message = "{httpMethod} {path} Resource Not Found")]
    protected partial void LogResourceNotFound(string httpMethod, string path);
}