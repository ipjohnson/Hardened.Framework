using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Kestrel.Runtime.Impl;

/// <summary>
/// Hardened's implementation of the server-to-application contract.
///
/// This is what <c>IServer.StartAsync</c> is given in place of ASP.NET's
/// <c>HostingApplication</c>. Kestrel calls <see cref="CreateContext"/> once per request with its
/// own feature collection, then <see cref="ProcessRequestAsync"/>, then
/// <see cref="DisposeContext"/> — the same three-step contract ASP.NET uses, with Hardened's
/// execution context in place of <c>HttpContext</c>.
///
/// The responsibilities <c>HostingApplication</c> would otherwise carry, and what happens to each
/// here:
///
/// <list type="bullet">
///   <item>Per-request DI scope — created here, disposed in <see cref="DisposeContext"/>.</item>
///   <item>Request logging and metrics — routed to Hardened's <c>IRequestLogger</c> and
///         <c>IMetricLogger</c> rather than ASP.NET's.</item>
///   <item>Unhandled exceptions — caught here (see <see cref="ProcessRequestAsync"/>).</item>
///   <item>Hosting diagnostics: <c>Activity</c>, <c>DiagnosticSource</c> and <c>EventSource</c>
///         are <b>not</b> raised. This is the significant omission — the standard OpenTelemetry
///         instrumentation subscribes to the <c>Microsoft.AspNetCore.Hosting</c> names, so an
///         existing OTel setup observes nothing from an application hosted this way.</item>
/// </list>
/// </summary>
[SingletonService]
public class HardenedHttpApplication : IHttpApplication<HardenedHttpApplication.RequestContext> {
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IMiddlewareService _middlewareService;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly IRequestLogger _requestLogger;

    public HardenedHttpApplication(
        IServiceProvider rootServiceProvider,
        IMiddlewareService middlewareService,
        IMetricLoggerProvider metricLoggerProvider,
        IRequestLogger requestLogger) {
        _rootServiceProvider = rootServiceProvider;
        _middlewareService = middlewareService;
        _metricLoggerProvider = metricLoggerProvider;
        _requestLogger = requestLogger;
    }

    /// <summary>
    /// Per-request state the server hands back on each callback. It exists to keep the DI scope
    /// paired with the execution context so the scope can be disposed at the right moment.
    /// </summary>
    public sealed class RequestContext {
        public required IServiceScope Scope { get; init; }

        public required FeatureExecutionContext Execution { get; init; }
    }

    public RequestContext CreateContext(IFeatureCollection contextFeatures) {
        var scope = _rootServiceProvider.CreateScope();

        var execution = new FeatureExecutionContext(
            _rootServiceProvider,
            scope.ServiceProvider,
            contextFeatures,
            _metricLoggerProvider.CreateLogger("kestrel-session"));

        _requestLogger.RequestBegin(execution);

        return new RequestContext { Scope = scope, Execution = execution };
    }

    /// <summary>
    /// Runs the middleware chain and completes the response.
    ///
    /// Exceptions are caught rather than allowed to propagate. Kestrel does have its own handler
    /// for an application that throws, but it treats the request as failed: it logs against the
    /// server rather than the application's own logger, and once the response has started it
    /// aborts the connection. Catching here means Hardened's <c>IRequestLogger</c> sees the
    /// failure and a 500 is still sent whenever the response has not yet been flushed.
    /// </summary>
    public async Task ProcessRequestAsync(RequestContext context) {
        var execution = context.Execution;

        try {
            await _middlewareService.GetExecutionChain(execution).Next();
        }
        catch (Exception exception) {
            _requestLogger.RequestFailed(execution, exception);

            // Once the response has started the status line is already on the wire and there is
            // nothing left to say; the connection will be torn down by the server.
            if (!execution.Response.ResponseStarted) {
                execution.Response.Status = 500;
            }
        }
        finally {
            // Required by Kestrel. A response that wrote no body — a 204, or a 500 set above —
            // never sends its headers otherwise, leaving the connection waiting on a request the
            // application already considers finished.
            await execution.CompleteAsync();
        }
    }

    public void DisposeContext(RequestContext context, Exception? exception) {
        var execution = context.Execution;

        execution.RequestMetrics.Record(
            RequestMetrics.TotalRequestDuration, execution.StartTime.GetElapsedMilliseconds());

        _requestLogger.RequestEnd(execution);

        context.Scope.Dispose();
    }
}
