using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.RequestFilter;

public interface IRequestFilterProvider {
    IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo);
}