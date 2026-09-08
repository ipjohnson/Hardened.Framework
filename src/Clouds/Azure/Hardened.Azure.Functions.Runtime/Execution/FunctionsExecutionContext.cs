using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Azure.Functions.Worker;

namespace Hardened.Azure.Functions.Runtime.Execution;

/// <summary>
/// One invocation's context.
/// </summary>
/// <remarks>
/// <para>
/// Every host builds its own, because the pieces a context holds come from somewhere different in
/// each: Kestrel reads its feature collection, Lambda an <c>ILambdaContext</c>, and this one the
/// worker's <see cref="FunctionContext"/>.
/// </para>
/// <para>
/// <b>The token is the host's.</b> Lambda has to manufacture a deadline token out of the time it
/// has left; the Functions host sends an explicit cancellation to the worker when it wants an
/// invocation stopped, and the worker exposes it as
/// <see cref="FunctionContext.CancellationToken"/>. Passing it through is the whole of what this
/// context adds, and it is what lets a handler abandon work when the host has given up on it.
/// </para>
/// </remarks>
public class FunctionsExecutionContext : IExecutionContext {
    public FunctionsExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        IKnownServices knownServices,
        IExecutionRequest request,
        IExecutionResponse response,
        FunctionContext functionContext,
        IMetricLogger? metricLogger = null) {
        RootServiceProvider = rootServiceProvider;
        RequestServices = requestServices;
        KnownServices = knownServices;
        Request = request;
        Response = response;
        FunctionContext = functionContext;
        RequestMetrics = metricLogger ?? new NullMetricsLogger();
        StartTime = MachineTimestamp.Now;
        CancellationToken = functionContext.CancellationToken;
    }

    /// <summary>
    /// The worker's own context for this invocation, for an adapter that settles or reports
    /// through it.
    /// </summary>
    public FunctionContext FunctionContext { get; }

    /// <summary>
    /// Which dispatch ends this invocation's chain, as the shim that started it said.
    /// </summary>
    public FunctionsDispatch Dispatch { get; init; } = FunctionsDispatch.Trigger;

    public IExecutionContext Clone(
        IExecutionRequest? request = null,
        IExecutionResponse? response = null,
        IServiceProvider? serviceProvider = null,
        IMetricLogger? metricLogger = null) {
        return new FunctionsExecutionContext(
            RootServiceProvider,
            serviceProvider ?? RequestServices,
            KnownServices,
            request ?? Request,
            response ?? Response,
            FunctionContext,
            metricLogger ?? RequestMetrics) {
            HandlerInstance = HandlerInstance,
            HandlerInfo = HandlerInfo,
            DefaultOutput = DefaultOutput,
            // The reference, not a copy: a fork is the same caller.
            CallerPrincipal = CallerPrincipal,
            // And the same invocation, so a fanned-out batch reports one id rather than ten.
            CorrelationId = CorrelationId,
            // A token replaced by a filter stays replaced on the forks it opens.
            CancellationToken = CancellationToken,
            Dispatch = Dispatch
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
