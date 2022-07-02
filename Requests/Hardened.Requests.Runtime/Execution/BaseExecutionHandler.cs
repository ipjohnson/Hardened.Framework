using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Execution
{
    public abstract class BaseExecutionHandler<TController> : IExecutionRequestHandler
    {
        private readonly Func<IExecutionContext, IExecutionFilter>[] _filters;
        private readonly DefaultOutputFunc? _outputFunc;

        protected BaseExecutionHandler(Func<IExecutionContext, IExecutionFilter>[] filters, DefaultOutputFunc? outputFunc = null)
        {
            _filters = filters;
            _outputFunc = outputFunc;
        }

        public abstract IExecutionRequestHandlerInfo HandlerInfo { get; }

        public IExecutionChain GetExecutionChain(IExecutionContext context)
        {
            context.HandlerInstance = context.RequestServices.GetRequiredService(typeof(TController));
            context.HandlerInfo = HandlerInfo;
            context.DefaultOutput = _outputFunc;

            var filterArray = new IExecutionFilter[_filters.Length];

            for (var i = 0; i < _filters.Length; i++)
            {
                filterArray[i] = _filters[i](context);
            }

            return new ExecutionChain(filterArray, context);
        }
    }
}
