using Amazon.Lambda.Core;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Aws.Lambda.Runtime.Execution;

/// <summary>
/// One invocation's context.
/// </summary>
/// <remarks>
/// <para>
/// Every host builds its own, because the pieces a context holds come from somewhere different in
/// each: Kestrel reads its feature collection, ASP.NET its <c>HttpContext</c>, and this one an
/// <see cref="ILambdaContext"/>.
/// </para>
/// <para>
/// <b>The deadline is the one thing only this host knows.</b> Lambda tells a function how long it
/// has left and then kills it, so a handler with no deadline token finds out it ran out of time by
/// not existing any more - no flush, no dead letter, no log line saying what it was doing.
/// <see cref="ForInvocation"/> turns <c>RemainingTime</c> into a token that trips shortly before
/// that, which is what lets a handler abandon work and answer.
/// </para>
/// </remarks>
public class LambdaExecutionContext : IExecutionContext {
    /// <summary>
    /// How long before the deadline the token trips.
    /// </summary>
    /// <remarks>
    /// Cancelling exactly at the deadline would be pointless: the handler would learn it was out of
    /// time at the moment Lambda killed it, with nothing left to do about it. The margin is what
    /// makes cancellation actionable - long enough to write a log line, report a batch failure or
    /// close a connection, short enough not to shorten a function that was going to finish.
    /// </remarks>
    public static readonly TimeSpan DefaultDeadlineMargin = TimeSpan.FromMilliseconds(500);

    public LambdaExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        IKnownServices knownServices,
        IExecutionRequest request,
        IExecutionResponse response,
        CancellationToken cancellationToken,
        IMetricLogger? metricLogger = null) {
        RootServiceProvider = rootServiceProvider;
        RequestServices = requestServices;
        KnownServices = knownServices;
        Request = request;
        Response = response;
        RequestMetrics = metricLogger ?? new NullMetricsLogger();
        StartTime = MachineTimestamp.Now;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// A token that trips <paramref name="margin"/> before Lambda's own deadline, and the source
    /// behind it for the caller to dispose.
    /// </summary>
    /// <remarks>
    /// <see cref="ILambdaContext.RemainingTime"/> is computed from the deadline the Runtime API sent
    /// with this invocation, so it is accurate per invocation rather than the configured timeout. A
    /// remaining time already inside the margin gives a token that is cancelled from the start,
    /// which is the honest answer: there is no time to do the work.
    /// </remarks>
    public static CancellationTokenSource ForInvocation(
        ILambdaContext context, TimeSpan? margin = null) {
        var remaining = context.RemainingTime - (margin ?? DefaultDeadlineMargin);

        var source = new CancellationTokenSource();

        if (remaining <= TimeSpan.Zero) {
            source.Cancel();
        }
        else {
            source.CancelAfter(remaining);
        }

        return source;
    }

    public IExecutionContext Clone(
        IExecutionRequest? request = null,
        IExecutionResponse? response = null,
        IServiceProvider? serviceProvider = null,
        IMetricLogger? metricLogger = null) {
        return new LambdaExecutionContext(
            RootServiceProvider,
            serviceProvider ?? RequestServices,
            KnownServices,
            request ?? Request,
            response ?? Response,
            CancellationToken,
            metricLogger ?? RequestMetrics) {
            HandlerInstance = HandlerInstance,
            HandlerInfo = HandlerInfo,
            DefaultOutput = DefaultOutput,
            // The reference, not a copy: a fork is the same caller.
            CallerPrincipal = CallerPrincipal,
            // And the same invocation, so a fanned-out batch reports one id rather than ten.
            CorrelationId = CorrelationId
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
