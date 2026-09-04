using Hardened.Requests.Abstract.Authorization;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Requests.Abstract.Execution;

public delegate Task DefaultOutputFunc(IExecutionContext executionContext);

/// <summary>
/// Object that holds all pertinent information for executing a request
/// </summary>
public interface IExecutionContext {
    IExecutionContext Clone(
        IExecutionRequest? request = null,
        IExecutionResponse? response = null,
        IServiceProvider? serviceProvider = null,
        IMetricLogger? metricLogger = null
        );
    
    /// <summary>
    /// Root service provider for the application
    /// </summary>
    IServiceProvider RootServiceProvider { get; }

    /// <summary>
    /// Set of request services
    /// </summary>
    IKnownServices KnownServices { get; }

    /// <summary>
    /// Service provider that is created/used for the life of the request
    /// </summary>
    IServiceProvider RequestServices { get; }

    /// <summary>
    /// Request parameters
    /// </summary>
    IExecutionRequest Request { get; }

    /// <summary>
    /// Response output
    /// </summary>
    IExecutionResponse Response { get; }

    /// <summary>
    /// The caller this request is running as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never null. A request starts as <see cref="AnonymousCallerPrincipal.Instance"/> and whatever
    /// validates a credential replaces it, so "no credential was presented" is a value rather than
    /// an absence and no reader needs a null check.
    /// </para>
    /// <para>
    /// The slot is settable; the value it holds is immutable. <c>Clone</c> copies the reference, so
    /// a forked chain observes the same caller - which is correct, because a retry is the same
    /// caller, and a retry after a revocation should fail.
    /// </para>
    /// </remarks>
    ICallerPrincipal CallerPrincipal { get; set; }

    /// <summary>
    /// One id for this request, for grouping everything it produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never null and never empty. It is the trace id when anything is collecting traces - so a log
    /// line and a span line up without anyone correlating two different identifiers - and a freshly
    /// generated value of the same shape when nothing is, because
    /// <c>ActivitySource.StartActivity</c> returns null with no listener and an id that only exists
    /// under a collector is missing exactly when it is most wanted.
    /// See <see cref="Diagnostics.CorrelationIdentifier"/>.
    /// </para>
    /// <para>
    /// Realized on first read rather than at construction: the host builds the context and only then
    /// calls <c>RequestBegin</c>, which is where the span starts.
    /// </para>
    /// <para>
    /// <c>Clone</c> carries it, for the same reason it carries
    /// <see cref="CallerPrincipal"/> - a fork is the same request, and a retried or forked chain
    /// that reported a second id would split one request's logs in two.
    /// </para>
    /// </remarks>
    string CorrelationId { get; }

    /// <summary>
    /// Handler for the call, will be null for middleware handlers
    /// </summary>
    object? HandlerInstance { get; set; }

    /// <summary>
    /// Get information about the 
    /// </summary>
    IExecutionRequestHandlerInfo? HandlerInfo { get; set; }

    /// <summary>
    /// Default output function, used to assign template
    /// </summary>
    DefaultOutputFunc? DefaultOutput { get; set; }

    /// <summary>
    /// Metric logger for the request
    /// </summary>
    IMetricLogger RequestMetrics { get; }

    /// <summary>
    /// Request StartTime
    /// </summary>
    MachineTimestamp StartTime { get; }

    /// <summary>
    /// What everything running for this request should stop on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transport's own token to begin with - <c>RequestAborted</c> on both web hosts, so a
    /// client that hangs up cancels the work it was waiting for. The Lambda runtimes in
    /// Hardened.Amz seed it with <see cref="System.Threading.CancellationToken.None"/> and have
    /// nothing to seed it from, so a disconnect is not observable there.
    /// </para>
    /// <para>
    /// Replaced for a span by <see cref="ReplaceCancellationToken"/>, which is how a deadline is
    /// expressed: a handler declaring a <c>CancellationToken</c> parameter has this copied into it
    /// as the request is bound, so a filter wanting to bound the handler has to change what this
    /// returns before the bind rather than hand a second token to something.
    /// </para>
    /// </remarks>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Makes <paramref name="token"/> what <see cref="CancellationToken"/> returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A method with a default rather than a setter on the property, and that is a
    /// compatibility decision rather than a stylistic one.</b> Adding a setter to the property is a
    /// breaking change to every implementation already compiled against this interface: the runtime
    /// wants a <c>set_CancellationToken</c> the older assembly does not carry, and the type fails
    /// to load on the first request. Hardened.Amz's three execution contexts are exactly that, and
    /// the template verification builds this framework against the newest published ones on
    /// purpose, so it is the case that has to keep working.
    /// </para>
    /// <para>
    /// <b>The default refuses rather than doing nothing.</b> A host that cannot replace the token
    /// cannot enforce a deadline, and silently ignoring a declared one would make a bounded
    /// operation look bounded while running forever. Refusing says so on the first request that
    /// declares a budget, and leaves every request that declares none untouched.
    /// </para>
    /// <para>
    /// Callers should use <c>CancellationScope</c> rather than this, so the previous token is put
    /// back. See its remarks for why the restore is load-bearing.
    /// </para>
    /// </remarks>
    void ReplaceCancellationToken(CancellationToken token) =>
        throw new NotSupportedException(
            GetType().Name + " cannot replace the request's cancellation token, so it cannot " +
            "enforce a deadline. A host supporting [Timeout] overrides " +
            nameof(ReplaceCancellationToken) + ".");
}