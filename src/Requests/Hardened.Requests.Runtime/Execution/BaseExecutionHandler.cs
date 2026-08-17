using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution;

public abstract class BaseExecutionHandler<TController> : IExecutionRequestHandler {
    private readonly Func<IExecutionContext, IExecutionFilter>[] _filters;
    private readonly DefaultOutputFunc? _outputFunc;

    protected BaseExecutionHandler(ExecutionHandlerSetup setup, DefaultOutputFunc? outputFunc = null) {
        HandlerInfo = setup.HandlerInfo;
        _filters = setup.Filters;
        _outputFunc = outputFunc;
    }

    /// <summary>
    /// The handler this chain was built for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Held here rather than overridden by the generated subclass, which is what makes it the
    /// amended one.</b> A subclass could only return the static field it declared - the handler as
    /// written, before conventions were applied - while the filter chain around it was built from
    /// the amended handler. The two would disagree for any handler a convention touched, and the
    /// disagreement would surface as a filter enforcing a requirement that
    /// <see cref="IExecutionContext.HandlerInfo"/> does not mention.
    /// </para>
    /// <para>
    /// Taking it off the setup the chain was built from makes that impossible to get wrong: there is
    /// one value, produced where the conventions ran.
    /// </para>
    /// </remarks>
    public IExecutionRequestHandlerInfo HandlerInfo { get; }

    public IExecutionChain GetExecutionChain(IExecutionContext context) {
        context.HandlerInfo = HandlerInfo;
        context.DefaultOutput = _outputFunc;

        return new ExecutionChain(_filters, context);
    }
}
