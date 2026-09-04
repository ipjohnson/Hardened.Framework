using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Web.Runtime.Conditional;

/// <summary>
/// Installs <see cref="ConditionalRequestFilter"/> on every GET handler.
/// </summary>
/// <remarks>
/// A provider registered by the web module, the way request decompression is registered by the
/// request module: consulted once per handler as its chain is built, on every host, with no
/// startup service to run. GET handlers only, because the routing table sends a HEAD to the GET
/// leaf and the conditionals mean something else on every other method. One filter instance
/// serves the whole application, since it holds nothing.
/// </remarks>
internal sealed class ConditionalRequestProvider : IRequestFilterProvider {
    private readonly ConditionalRequestFilter _filter = new();

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        if (!ConditionalRequestFilter.IsGetOrHead(handlerInfo.Method)) {
            yield break;
        }

        yield return new RequestFilterInfo(_ => _filter, FilterOrder.Conditional);
    }
}
