using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Execution;

namespace Hardened.Requests.Runtime.Middleware;

[SingletonService(Using = RegistrationType.Try)]
public class MiddlewareService : IMiddlewareService {
    private static readonly ResponseFinalizerFilter Finalizer = new();

    /// <summary>
    /// The finalizer first, so it wraps everything a host or an application registers after it.
    /// </summary>
    /// <remarks>
    /// Seeded here rather than added by each host because there are five of them, and a host that
    /// forgot would silently go back to answering middleware refusals with an empty body. It holds
    /// no state, so one instance serves every request.
    /// </remarks>
    private readonly List<Func<IExecutionContext, IExecutionFilter>> _filters =
        new() { _ => Finalizer };

    public void Use(Func<IExecutionContext, IExecutionFilter> middlewareFunc) {
        _filters.Add(middlewareFunc);
    }

    public IExecutionChain GetExecutionChain(IExecutionContext context) {
        return new ExecutionChain(_filters, context);
    }
}