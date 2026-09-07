namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// What a host does when the middleware chain throws, which is the one thing the five drivers
/// genuinely disagreed about.
/// </summary>
public enum HostFailurePolicy {
    /// <summary>
    /// Answer 500 if the response has not started, tell <c>IRequestLogger</c>, and return
    /// normally. The web hosts, because the server's own handler would log against the server
    /// rather than the application and abort the connection mid-body.
    /// </summary>
    Answer500,

    /// <summary>
    /// Tell <c>IRequestLogger</c> and rethrow. The Lambda drivers, because the runtime marking an
    /// invocation failed is the existing contract: inventing a 500 hides the failure from retries
    /// and the dead letter queue.
    /// </summary>
    Rethrow
}

/// <summary>
/// Everything a host does around the middleware chain, in one implementation.
/// </summary>
/// <remarks>
/// <para>
/// It was written five times: <c>HardenedHttpApplication</c>, <c>AspNetCoreRequestHandler</c>,
/// <c>ApiGatewayEventProcessor</c>, <c>StreamingEventProcessor</c> and
/// <c>LambdaFunctionImplService</c>, plus a sixth and seventh in the test and benchmark harnesses.
/// No line of it names a transport, and the cost of that was the same defect fixed three times
/// independently: a request that threw lost its duration, its end log line and its metrics,
/// because the close-out ran as straight-line statements after the chain rather than in a
/// <c>finally</c>. Three hosts were fixed one at a time and the other two were not named in any of
/// the three commits.
/// </para>
/// <para>
/// Three steps rather than one call, because <c>IHttpApplication&lt;TContext&gt;</c> hands Kestrel
/// the lifecycle in pieces: <c>CreateContext</c> has to return before <c>ProcessRequestAsync</c>
/// is called, so a single <c>Run</c> cannot serve it. <see cref="Run"/> is the same three in order
/// for the hosts that do own the whole invocation.
/// </para>
/// <para>
/// The host still owns what is genuinely its own: the scope, the request and response, and any
/// work that has to happen between the chain finishing and the response being handed back - which
/// on Lambda is completing a response stream or rewinding a buffer, and on Kestrel is
/// <c>CompleteAsync</c>.
/// </para>
/// </remarks>
public interface IRequestExecutor {
    /// <summary>
    /// Opens the request. Call once, after the context is built and before <see cref="RunChain"/>.
    /// </summary>
    void Begin(IExecutionContext context);

    /// <summary>
    /// Runs the middleware chain, applying <paramref name="onFailure"/> to anything it throws.
    /// </summary>
    Task RunChain(IExecutionContext context, HostFailurePolicy onFailure);

    /// <summary>
    /// Closes the request out: the total duration, the end log line, and disposal of the metric
    /// logger. Call from a <c>finally</c>, or from the callback the server guarantees.
    /// </summary>
    /// <remarks>
    /// Does not dispose the scope. The host created it and the hosts disagree about when it ends -
    /// Kestrel holds it across three callbacks, Lambda has it in an <c>await using</c>, and
    /// ASP.NET Core never made one because the request already has one.
    /// </remarks>
    void End(IExecutionContext context);

    /// <summary>
    /// <see cref="Begin"/>, <see cref="RunChain"/> and <see cref="End"/>, for a host that owns the
    /// whole invocation in one call. <see cref="End"/> runs in a <c>finally</c>.
    /// </summary>
    Task Run(IExecutionContext context, HostFailurePolicy onFailure);
}
