using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Filters;

[SingletonService(Using = RegistrationType.Try)]
public class InstanceFilterProvider : IInstanceFilterProvider {
    public IExecutionFilter ProvideFilter<T>(IServiceProvider rootProvider) {
        return new InstanceFilter<T>();
    }
}