using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Middleware;

public interface IMiddlewareService {
    void Use(Func<IExecutionContext, IExecutionFilter> middlewareFunc);

    IExecutionChain GetExecutionChain(IExecutionContext context);
}