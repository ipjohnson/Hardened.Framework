using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Filters;

public class SingleFilterProvider : IRequestFilterProvider {
    private readonly Func<IExecutionRequestHandlerInfo, RequestFilterInfo?> _filterFunc;

    public SingleFilterProvider(Func<IExecutionRequestHandlerInfo, RequestFilterInfo?> filterFunc) {
        _filterFunc = filterFunc;
    }

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        var filterInfo = _filterFunc(handlerInfo);

        if (filterInfo != null) {
            yield return filterInfo;
        }
    }
}