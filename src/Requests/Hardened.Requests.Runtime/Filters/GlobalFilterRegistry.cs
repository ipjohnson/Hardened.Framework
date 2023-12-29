using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Filters;

public class GlobalFilterRegistry : IGlobalFilterRegistry {
    private readonly List<IRequestFilterProvider> _filterProviders;

    public GlobalFilterRegistry(IEnumerable<IRequestFilterProvider> filterProviders) {
        _filterProviders = new List<IRequestFilterProvider>(filterProviders);
    }

    public void RegisterFilter(IExecutionFilter filter, int order = FilterOrder.DefaultValue) {
        var filterInfo = new RequestFilterInfo(_ => filter, order);

        RegisterFilter(_ => filterInfo);
    }

    public void RegisterFilter(Func<IExecutionRequestHandlerInfo, RequestFilterInfo?> filterFunc) {
        _filterProviders.Add(new SingleFilterProvider(filterFunc));
    }

    public List<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo requestHandlerInfo) {
        var returnList = new List<RequestFilterInfo>();

        foreach (var filterProvider in _filterProviders) {
            foreach (var filterInfo in filterProvider.GetFilters(requestHandlerInfo)) {
                returnList.Add(filterInfo);
            }
        }

        return returnList;
    }
}