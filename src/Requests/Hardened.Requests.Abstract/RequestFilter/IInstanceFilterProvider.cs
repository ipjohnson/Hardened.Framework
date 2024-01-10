using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.RequestFilter;

public interface IInstanceFilterProvider {
    IExecutionFilter ProvideFilter<T>(IServiceProvider rootProvider);
}