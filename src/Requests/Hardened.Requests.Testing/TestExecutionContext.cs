using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Requests.Testing;

public class TestExecutionContext : IExecutionContext {
    /// <param name="metricLogger">
    /// The sink measurements recorded during the request land in. Optional, and a
    /// <see cref="NullMetricsLogger"/> when omitted, which is what this always used to build.
    ///
    /// Hardcoding it meant the harness had no metric seam at all: an application could not write a
    /// test asserting what a request emitted, and <see cref="Clone"/> silently discarded the logger
    /// it was handed - so a test about per-fork attribution would have passed against any behaviour
    /// whatsoever.
    /// </param>
    public TestExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        IKnownServices knownServices,
        IExecutionRequest request,
        IExecutionResponse response,
        CancellationToken cancellationToken,
        IMetricLogger? metricLogger = null) {
        RootServiceProvider = rootServiceProvider;
        RequestServices = requestServices;
        Request = request;
        Response = response;
        KnownServices = knownServices;
        RequestMetrics = metricLogger ?? new NullMetricsLogger();
        StartTime = MachineTimestamp.Now;
        CancellationToken = cancellationToken;
    }

    public IExecutionContext Clone(
        IExecutionRequest? request,
        IExecutionResponse? response,
        IServiceProvider? serviceProvider,
        IMetricLogger? metricLogger) {
        return new TestExecutionContext(
            RootServiceProvider,
            serviceProvider ?? RequestServices,
            KnownServices,
            request ?? Request,
            response ?? Response,
            CancellationToken,
            metricLogger ?? RequestMetrics) {
            HandlerInstance = HandlerInstance,
            HandlerInfo = HandlerInfo,
            // The reference, not a copy: a fork is the same caller.
            CallerPrincipal = CallerPrincipal,
            // And the same request, so it reports one id rather than two.
            CorrelationId = CorrelationId,
        };
    }

    public IServiceProvider RootServiceProvider { get; }

    public IKnownServices KnownServices { get; }

    public IServiceProvider RequestServices { get; }

    public IExecutionRequest Request { get; }

    public IExecutionResponse Response { get; }

    public ICallerPrincipal CallerPrincipal { get; set; } = AnonymousCallerPrincipal.Instance;

    private string? _correlationId;

    /// <inheritdoc />
    public string CorrelationId {
        get => _correlationId ??= CorrelationIdentifier.ForCurrentTrace();
        init => _correlationId = value;
    }

    public object? HandlerInstance { get; set; }

    public IExecutionRequestHandlerInfo? HandlerInfo { get; set; }

    public DefaultOutputFunc? DefaultOutput { get; set; }

    public IMetricLogger RequestMetrics { get; }

    public MachineTimestamp StartTime { get; }

    public CancellationToken CancellationToken { get; set; }

    /// <inheritdoc />
    public void ReplaceCancellationToken(CancellationToken token) => CancellationToken = token;
}