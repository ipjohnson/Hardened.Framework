using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.RequestFilter;

public interface IGlobalFilterRegistry {
    void RegisterFilter(IExecutionFilter filter, int order = FilterOrder.DefaultValue);

    void RegisterFilter(Func<IExecutionRequestHandlerInfo, RequestFilterInfo?> filterFunc);

    List<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo requestHandlerInfo);
}