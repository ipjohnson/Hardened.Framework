using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;

namespace Hardened.Requests.Runtime.Filters;

[SingletonService(Using = RegistrationType.Try)]
public class GlobalFilterRegistry : IGlobalFilterRegistry {
    private readonly List<IRequestFilterProvider> _filterProviders;

    public GlobalFilterRegistry(IEnumerable<IRequestFilterProvider> filterProviders) {
        _filterProviders = new List<IRequestFilterProvider>(filterProviders);
    }

    public void RegisterFilter(IExecutionFilter filter, int order = FilterOrder.DefaultValue) {
        // Named for the instance, since it is to hand. The closure below would otherwise name the
        // registry, which registered nothing on its own account.
        var filterInfo = new RequestFilterInfo(_ => filter, order, FilterNames.Of(filter));

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