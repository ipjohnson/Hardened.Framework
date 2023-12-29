using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Middleware;

public interface IMiddlewareProvider {
    IEnumerable<Func<IServiceProvider, IExecutionFilter>> ProvideMiddleware();
}