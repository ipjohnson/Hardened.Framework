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
    /// Cancellation token, only applicable on some platforms
    /// </summary>
    CancellationToken CancellationToken { get; }
}