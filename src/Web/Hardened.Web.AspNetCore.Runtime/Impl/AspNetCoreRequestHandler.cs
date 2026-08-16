using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.AspNetCore.Http;

namespace Hardened.Web.AspNetCore.Runtime.Impl;

public interface IAspNetCoreRequestHandler {
    Task HandleRequest(HttpContext context, RequestDelegate requestDelegate);
}

[TransientService]
public class AspNetCoreRequestHandler : IAspNetCoreRequestHandler {
    private IMetricLoggerProvider _metricLoggerProvider;
    private IMiddlewareService _middlewareService;
    private IRequestLogger _requestLogger;

    public AspNetCoreRequestHandler(
        IMetricLoggerProvider metricLoggerProvider,
        IMiddlewareService middlewareService,
        IRequestLogger requestLogger) {
        _metricLoggerProvider = metricLoggerProvider;
        _middlewareService = middlewareService;
        _requestLogger = requestLogger;
    }

    /// <summary>
    /// Runs the Hardened chain, and hands the request on if the chain produced nothing.
    ///
    /// The request logging and duration metric around it are what every other host already does —
    /// <c>ApiGatewayEventProcessor</c> on Lambda and <c>TestWebApp</c> in the test harness both
    /// bracket the chain this way, and <c>HardenedHttpApplication</c> does on Kestrel. This host
    /// did not, so an ASP.NET-hosted application saw <c>RequestMapped</c> and <c>RequestFailed</c>
    /// from inside the pipeline but never a begin, an end, or a <c>TotalRequestDuration</c>.
    /// </summary>
    public async Task HandleRequest(HttpContext context, RequestDelegate requestDelegate) {
        var requestStartTimestamp = MachineTimestamp.Now;

        var executionContext = GetExecutionContext(context, _metricLoggerProvider);

        _requestLogger.RequestBegin(executionContext);

        try {
            var executionChain = _middlewareService.GetExecutionChain(executionContext);

            await executionChain.Next();

            if (!Answered(executionContext)) {
                await requestDelegate(context);
            }
        }
        finally {
            // In a finally because these ran as straight-line statements after the chain, so an
            // exception escaping to ASP.NET's own handler took the whole close-out with it: no
            // duration, no end, and no flush. A request that threw is one worth having a record of.
            executionContext.RequestMetrics.Record(
                RequestMetrics.TotalRequestDuration, requestStartTimestamp.GetElapsedMilliseconds());

            _requestLogger.RequestEnd(executionContext);

            // The logger is per request and nothing else owns it. Disposal is how a provider learns
            // the request finished - EmbeddedMetricLogger writes its EMF line here - so without it
            // any provider that emits on completion emitted nothing at all.
            executionContext.RequestMetrics.Dispose();
        }
    }

    /// <summary>
    /// Whether the Hardened chain answered this request, and so whether it stops here rather than
    /// continuing down the ASP.NET pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to ask <c>context.Response.HasStarted</c>, which turns true only once response
    /// <em>bytes</em> have flushed. A status and headers flush nothing, so every response with no
    /// body fell through to the terminal delegate and came back as ASP.NET's 404: a 204, a 308
    /// redirect, a 405 with its <c>Allow</c> header, a <c>HEAD</c>, and any 200 from a handler
    /// returning <c>Task</c>. The side effect had already happened — a <c>DELETE</c> really did
    /// delete — and the caller was told the resource did not exist. The Kestrel host was unaffected
    /// because it has no fallthrough to fall into.
    /// </para>
    /// <para>
    /// Four signals, because the pipeline has four ways to answer and they set different things:
    /// </para>
    /// <list type="bullet">
    /// <item><c>HandlerInfo</c> — routing selected a handler. Set by
    /// <c>WebExecutionHandlerService.Dispatch</c> before the handler runs, so it holds even when the
    /// handler writes nothing at all. This is the one the old check had no equivalent of.</item>
    /// <item><c>Status</c> — static content (200 or 304), a 405, a 308 trailing-slash redirect, or a
    /// handler that set one itself.</item>
    /// <item><c>ResponseValue</c> — a handler returned something not yet serialized.</item>
    /// <item><c>ResponseStarted</c> — bytes are already on the wire. The old signal, still
    /// sufficient, now merely no longer necessary.</item>
    /// </list>
    /// <para>
    /// A path Hardened declares nothing for reaches none of them, because this host registers
    /// <see cref="AspNetResourceNotFoundHandler"/> in place of the framework's terminal one exactly
    /// so the status stays unset. That request falls through — which is the reason the fallthrough
    /// exists, so static files, another middleware or MVC behind <c>UseHardened()</c> get their
    /// turn, and ASP.NET's own 404 answers if none of them do.
    /// </para>
    /// </remarks>
    private static bool Answered(IExecutionContext executionContext) {
        var response = executionContext.Response;

        return executionContext.HandlerInfo != null
               || response.Status.HasValue
               || response.ResponseValue != null
               || response.ResponseStarted;
    }

    private IExecutionContext GetExecutionContext(
        HttpContext context,
        IMetricLoggerProvider metricLoggerProvider) {
        return new AspNetExecutionContext(context, metricLoggerProvider.CreateLogger("asp-net-session"));
    }
}
