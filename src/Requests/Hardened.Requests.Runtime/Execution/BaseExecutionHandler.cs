using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Execution;

public abstract class BaseExecutionHandler<TController> : IExecutionRequestHandler {
    private readonly Func<IExecutionContext, IExecutionFilter>[] _filters;
    private readonly DefaultOutputFunc? _outputFunc;

    protected BaseExecutionHandler(Func<IExecutionContext, IExecutionFilter>[] filters,
        DefaultOutputFunc? outputFunc = null) {
        _filters = filters;
        _outputFunc = outputFunc;
    }

    public abstract IExecutionRequestHandlerInfo HandlerInfo { get; }

    public IExecutionChain GetExecutionChain(IExecutionContext context) {
        context.HandlerInfo = HandlerInfo;
        context.DefaultOutput = _outputFunc;

        return new ExecutionChain(_filters, context);
    }
}