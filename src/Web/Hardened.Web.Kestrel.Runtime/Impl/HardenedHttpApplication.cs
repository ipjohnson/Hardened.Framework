using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
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
///   <item>Unhandled exceptions — <c>IRequestExecutor</c>, under
///         <see cref="HostFailurePolicy.Answer500"/>.</item>
///   <item>Hosting diagnostics: <c>Activity</c>, <c>DiagnosticSource</c> and <c>EventSource</c>
///         are <b>not</b> raised. This is the significant omission — the standard OpenTelemetry
///         instrumentation subscribes to the <c>Microsoft.AspNetCore.Hosting</c> names, so an
///         existing OTel setup observes nothing from an application hosted this way.</item>
/// </list>
/// </summary>
[SingletonService]
public class HardenedHttpApplication : IHttpApplication<HardenedHttpApplication.RequestContext> {
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IRequestExecutor _executor;
    private readonly IMetricLoggerProvider _metricLoggerProvider;

    public HardenedHttpApplication(
        IServiceProvider rootServiceProvider,
        IRequestExecutor executor,
        IMetricLoggerProvider metricLoggerProvider) {
        _rootServiceProvider = rootServiceProvider;
        _executor = executor;
        _metricLoggerProvider = metricLoggerProvider;
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

        _executor.Begin(execution);

        return new RequestContext { Scope = scope, Execution = execution };
    }

    /// <summary>
    /// Runs the middleware chain and completes the response.
    ///
    /// <see cref="HostFailurePolicy.Answer500"/> rather than letting a throw propagate. Kestrel
    /// does have its own handler for an application that throws, but it treats the request as
    /// failed: it logs against the server rather than the application's own logger, and once the
    /// response has started it aborts the connection.
    /// </summary>
    public async Task ProcessRequestAsync(RequestContext context) {
        var execution = context.Execution;

        try {
            await _executor.RunChain(execution, HostFailurePolicy.Answer500);
        }
        finally {
            // Required by Kestrel. A response that wrote no body — a 204, or the 500 the failure
            // policy sets — never sends its headers otherwise, leaving the connection waiting on a
            // request the application already considers finished.
            await execution.CompleteAsync();
        }
    }

    /// <summary>
    /// Closes the request out. Kestrel calls this for every request it created a context for,
    /// including one that threw, which is the guarantee <see cref="IRequestExecutor.End"/> would
    /// otherwise want a <c>finally</c> for.
    /// </summary>
    public void DisposeContext(RequestContext context, Exception? exception) {
        _executor.End(context.Execution);

        context.Scope.Dispose();
    }
}
