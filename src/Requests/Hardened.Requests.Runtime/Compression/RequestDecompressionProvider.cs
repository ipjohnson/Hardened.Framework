using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Compression;

/// <summary>
/// Installs <see cref="RequestDecompressionFilter"/> on every handler.
/// </summary>
/// <remarks>
/// A provider registered by the request module rather than a startup service, so nothing is
/// resolved until a request arrives: the filter reads the configuration on its first request, the
/// way the response filter and the response cache do. One filter instance serves the whole
/// application, since it holds nothing but that.
/// </remarks>
internal sealed class RequestDecompressionProvider : IRequestFilterProvider {
    private readonly RequestDecompressionFilter _filter = new();

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return new RequestFilterInfo(
            _ => _filter, FilterOrder.Before + FilterOrder.ResponseCache, nameof(RequestDecompressionFilter));
    }
}
