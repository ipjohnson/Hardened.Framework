using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution;

/// <summary>
/// Everything a handler needs to serve requests: the filter chain, and the handler it was built for.
/// </summary>
/// <remarks>
/// <para>
/// Both together because the second is not what the caller passed in. Conventions are applied while
/// the chain is assembled, so what the generated handler declares and what it actually is are two
/// values, and only the second may be used from then on. Returning just the filters would leave the
/// declared one as the only thing the handler could hold - which is exactly the drift this exists to
/// prevent.
/// </para>
/// <para>
/// A struct, and constructed once per handler at startup. It exists to get two values out of one
/// call, not to be stored.
/// </para>
/// </remarks>
public readonly struct ExecutionHandlerSetup {
    public ExecutionHandlerSetup(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, IExecutionFilter>[] filters) {
        HandlerInfo = handlerInfo;
        Filters = filters;
    }

    /// <summary>
    /// The handler as it ended up - what it declared, plus whatever conventions added.
    /// </summary>
    public IExecutionRequestHandlerInfo HandlerInfo { get; }

    public Func<IExecutionContext, IExecutionFilter>[] Filters { get; }
}
