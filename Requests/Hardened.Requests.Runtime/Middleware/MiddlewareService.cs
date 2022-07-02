using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Execution;

namespace Hardened.Requests.Runtime.Middleware
{
    public class MiddlewareService : IMiddlewareService
    {
        private readonly List<Func<IExecutionContext, IExecutionFilter>> _filters = new ();
        
        public void Use(Func<IExecutionContext, IExecutionFilter> middlewareFunc)
        {
            _filters.Add(middlewareFunc);
        }

        public IExecutionChain GetExecutionChain(IExecutionContext context)
        {
            var filters = new IExecutionFilter[_filters.Count];

            for (var i = 0; i < filters.Length; i++)
            {
                filters[i] = _filters[i](context);
            }

            return new ExecutionChain(filters, context);
        }
    }
}
