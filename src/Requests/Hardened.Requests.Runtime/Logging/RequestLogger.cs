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

    private readonly ILogger<RequestLogger> _logger;

    public RequestLogger(ILogger<RequestLogger> logger) {
        _logger = logger;
    }

    public void RequestBegin(IExecutionContext context) {
        LogRequestStarted(context.Request.Method, context.Request.Path);

        // A dimension for whatever provider is behind IMetricLogger, independent of tracing. What
        // becomes of it is the provider's decision: the Meter bridge makes it a histogram dimension,
        // and EMF promotes nothing unless the application opted the tag in, because a CloudWatch
        // dimension is billed per distinct value.
        context.RequestMetrics.Tag("http.request.method", context.Request.Method);

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

        if (span is null) {
            return;
        }

        span.SetTag("http.request.method", context.Request.Method);
        span.SetTag("url.path", context.Request.Path);

        _spans.AddOrUpdate(context, span);
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

        if (!_spans.TryGetValue(context, out var span)) {
            return;
        }

        _spans.Remove(context);

        span.SetTag("http.response.status_code", status);

        // 4xx is the caller's mistake, not the server's. The conventions leave a server span Unset
        // for those and reserve Error for 5xx, so that a trace backend's error rate means "this
        // service failed" rather than "someone sent a bad request".
        if (status >= 500) {
            span.SetStatus(ActivityStatusCode.Error);
        }

        span.Stop();
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