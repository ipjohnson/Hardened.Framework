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

        var executionChain = _middlewareService.GetExecutionChain(executionContext);

        await executionChain.Next();

        if (!context.Response.HasStarted) {
            await requestDelegate(context);
        }

        executionContext.RequestMetrics.Record(
            RequestMetrics.TotalRequestDuration, requestStartTimestamp.GetElapsedMilliseconds());

        _requestLogger.RequestEnd(executionContext);
    }

    private IExecutionContext GetExecutionContext(
        HttpContext context,
        IMetricLoggerProvider metricLoggerProvider) {
        return new AspNetExecutionContext(context, metricLoggerProvider.CreateLogger("asp-net-session"));
    }
}
