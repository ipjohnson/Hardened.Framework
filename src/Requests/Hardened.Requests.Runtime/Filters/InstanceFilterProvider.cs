using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Filters;

public class InstanceFilterProvider : IInstanceFilterProvider {
    public IExecutionFilter ProvideFilter<T>(IServiceProvider rootProvider) {
        return new InstanceFilter<T>();
    }
}