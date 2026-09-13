using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Filters;

[SingletonService(Using = RegistrationType.Try)]
public class InstanceFilterProvider : IInstanceFilterProvider {
    /// <remarks>
    /// <c>object</c> is the generator's signal that the handler is a static method, and it can mean
    /// nothing else: a controller type reaches here because a method on it carries a verb
    /// attribute, and <c>object</c> declares none. A replacement for this service has to honour the
    /// same reading or static handlers lose the only filter that sets their
    /// <c>HandlerInstance</c> - hence <see cref="StaticInstanceFilter"/> being public to delegate
    /// to.
    /// </remarks>
    public IExecutionFilter ProvideFilter<T>(IServiceProvider rootProvider) {
        return typeof(T) == typeof(object)
            ? StaticInstanceFilter.Instance
            : InstanceFilter<T>.Instance;
    }
}