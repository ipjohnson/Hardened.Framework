using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Web.Runtime.CacheControl;

namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Sets the <c>Cache-Control</c> header on the responses of the handler, or of every handler on the
/// controller, it is applied to.
/// </summary>
/// <remarks>
/// <para>
/// This did nothing until it implemented <see cref="IRequestFilterProvider"/>. The generator has
/// always copied it into the handler's metadata array, but <c>ExecutionHelper.GetFilterInfo</c>
/// sieves that array for filter providers and discards the rest - so the attribute compiled,
/// travelled the whole way to <c>IExecutionRequestHandlerInfo.Metadata</c>, and was thrown away
/// one step from being used. It is still in the metadata for anything that wants to read it.
/// </para>
/// <para>
/// The filter is built once per handler rather than per request: a provider is consulted when the
/// handler is first constructed, and the value here cannot change after that.
/// </para>
/// </remarks>
public class CacheControlAttribute : Attribute, IRequestFilterProvider {
    public int MaxAge { get; set; } = 0;

    public CacheControlEnum Type { get; set; } = CacheControlEnum.MaxAge | CacheControlEnum.Public;

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        var headerValue = CacheControlHeader.Format(Type, MaxAge);

        // No directive set is a header with nothing to say, so none is written.
        if (headerValue == null) {
            yield break;
        }

        var filter = new CacheControlFilter(headerValue);

        yield return new RequestFilterInfo(_ => filter, FilterOrder.BeforeSerialization, nameof(CacheControlFilter));
    }
}
