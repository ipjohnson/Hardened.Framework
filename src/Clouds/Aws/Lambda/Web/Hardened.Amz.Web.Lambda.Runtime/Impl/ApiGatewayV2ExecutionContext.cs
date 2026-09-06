using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

internal class ApiGatewayV2ExecutionContext : IExecutionContext {
    public ApiGatewayV2ExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        IKnownServices knownServices,
        IExecutionRequest request,
        IExecutionResponse response,
        IMetricLogger requestMetrics,
        MachineTimestamp startTime) {
        RootServiceProvider = rootServiceProvider;
        RequestServices = requestServices;
        Request = request;
        Response = response;
        RequestMetrics = requestMetrics;
        StartTime = startTime;
        KnownServices = knownServices;
    }

    public IExecutionContext Clone(
        IExecutionRequest? request,
        IExecutionResponse? response,
        IServiceProvider? serviceProvider,
        IMetricLogger? metricLogger) {
        return new ApiGatewayV2ExecutionContext(RootServiceProvider, 
            serviceProvider?? RequestServices,
            KnownServices,
            request ?? Request,
            response ?? Response,
            metricLogger ?? RequestMetrics,
            StartTime) {
            // The reference, not a copy: a fork is the same caller.
            CallerPrincipal = CallerPrincipal,
            // And the same request, so it reports one id rather than two.
            CorrelationId = CorrelationId
        };
    }

    public IServiceProvider RootServiceProvider { get; }
    public IKnownServices KnownServices { get; }

    public IServiceProvider RequestServices { get; }

    public IExecutionRequest Request { get; }

    public IExecutionResponse Response { get; }

    /// <inheritdoc />
    public ICallerPrincipal CallerPrincipal { get; set; } = AnonymousCallerPrincipal.Instance;

    private string? _correlationId;

    /// <inheritdoc />
    /// <remarks>
    /// Realized on first read rather than at construction, so it is the trace id when anything is
    /// collecting traces - the host starts the span after building the context.
    /// </remarks>
    public string CorrelationId {
        get => _correlationId ??= CorrelationIdentifier.ForCurrentTrace();
        init => _correlationId = value;
    }

    public object? HandlerInstance { get; set; }

    public IExecutionRequestHandlerInfo? HandlerInfo { get; set; }

    public DefaultOutputFunc? DefaultOutput { get; set; }

    public IMetricLogger RequestMetrics { get; }

    public MachineTimestamp StartTime { get; }

    /// <summary>
    /// Seeded with <see cref="System.Threading.CancellationToken.None"/>, because Lambda surfaces
    /// no signal for a caller that hung up: the invocation runs to completion whether or not
    /// anyone is still waiting for it.
    /// </summary>
    /// <remarks>
    /// Settable through <see cref="ReplaceCancellationToken"/> all the same, which is what lets
    /// <c>[Timeout]</c> bound a handler here. A deadline needs nothing from the transport - the
    /// filter links a source and cancels it on its own timer - so the budget works even though the
    /// disconnect it links from never fires.
    /// </remarks>
    public CancellationToken CancellationToken { get; private set; } = CancellationToken.None;

    /// <inheritdoc />
    public void ReplaceCancellationToken(CancellationToken token) => CancellationToken = token;
}