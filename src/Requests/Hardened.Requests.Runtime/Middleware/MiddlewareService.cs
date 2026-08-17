using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Execution;

namespace Hardened.Requests.Runtime.Middleware;

[SingletonService(Using = RegistrationType.Try)]
public class MiddlewareService : IMiddlewareService {
    private static readonly ResponseFinalizerFilter Finalizer = new();

    private static readonly CorrelationHeaderFilter CorrelationHeader = new();

    /// <summary>
    /// The finalizer first, so it wraps everything a host or an application registers after it, then
    /// the correlation header, so the id is on the response before anything can short-circuit.
    /// </summary>
    /// <remarks>
    /// Seeded here rather than added by each host because there are five of them, and a host that
    /// forgot would silently go back to answering middleware refusals with an empty body - or, for
    /// the second one, stop returning an id on exactly the refusals somebody wants to ask about.
    /// Neither holds state, so one instance of each serves every request.
    /// </remarks>
    private readonly List<Func<IExecutionContext, IExecutionFilter>> _filters =
        new() { _ => Finalizer, _ => CorrelationHeader };

    public void Use(Func<IExecutionContext, IExecutionFilter> middlewareFunc) {
        _filters.Add(middlewareFunc);
    }

    public IExecutionChain GetExecutionChain(IExecutionContext context) {
        return new ExecutionChain(_filters, context);
    }
}