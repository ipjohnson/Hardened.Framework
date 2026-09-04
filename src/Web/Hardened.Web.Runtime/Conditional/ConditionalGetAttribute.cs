using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Web.Runtime.Conditional;

/// <summary>
/// Answers a conditional GET at this operation, or at every operation on this class.
///
/// <code>
/// [Get("/rates/{symbol}")]
/// [CacheResponse&lt;VaryByRoute&gt;(Duration = 3600, Tags = ["rates"])]
/// [ConditionalGet]
/// public Rate Read(string symbol) =&gt; _rates.Latest(symbol);
/// </code>
///
/// <para>
/// A caller holding the response's <c>ETag</c> or <c>Last-Modified</c> and sending it back in
/// <c>If-None-Match</c> or <c>If-Modified-Since</c> is answered 304 with no body. A response
/// that already carries a tag - a handler wrote one, or the entry the response cache captured
/// carries one - is passed straight through; a response carrying none is held back and tagged
/// over the bytes it sends, which is a buffer and a hash per response and the reason this is
/// declared rather than assumed.
/// </para>
/// <para>
/// One declaration per operation, on the method or on its class; a second finds the body already
/// wrapped at run time and stands down. An operation carrying this is left alone by the
/// application-wide default that <c>[Enable&lt;ConditionalGet&gt;]</c> installs.
/// </para>
/// <para>
/// GET handlers only. The routing table sends a HEAD to the GET leaf, and on any other method the
/// conditionals mean a 412, which this does not answer - so a class-level declaration on a
/// controller that also writes installs nothing on the writes.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ConditionalGetAttribute : Attribute, IRequestFilterProvider {
    private readonly ConditionalGetFilter _filter = new();

    /// <summary>
    /// One filter instance per declaration, shared by every request, at
    /// <see cref="FilterOrder.Conditional"/>.
    /// </summary>
    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        if (!ConditionalGetFilter.IsGetOrHead(handlerInfo.Method)) {
            yield break;
        }

        yield return new RequestFilterInfo(_ => _filter, FilterOrder.Conditional, nameof(ConditionalGetFilter));
    }

    /// <summary>
    /// Whether the handler declares this itself, on the method or on its class. What the
    /// application-wide default checks before standing down.
    /// </summary>
    public static bool Declares(IExecutionRequestHandlerInfo handlerInfo) {
        foreach (var item in handlerInfo.Metadata) {
            if (item is ConditionalGetAttribute) {
                return true;
            }
        }

        return false;
    }
}
