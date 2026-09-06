using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Abstract.Headers;

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
/// <para>
/// Nothing on a handler that streams either. Tagging a response means holding it back to hash
/// it, which turns an <c>IAsyncEnumerable&lt;T&gt;</c> answered item by item into one buffered
/// body sent after the last item - so a declaration reaching such a handler, from its class or
/// from <c>[Enable&lt;ConditionalGet&gt;]</c>, installs nothing, the way it installs nothing on
/// a write.
/// </para>
/// </summary>
// What the document says about an operation carrying this. It published nothing at all: no 304,
// no ETag and no If-None-Match, so a generated client could not make the conditional request the
// filter exists to answer, and the 304 it would be sent had no branch. GET and HEAD only, and not
// on a streamed handler, because those are exactly the operations GetFilters installs nothing on.
[AnswersStatus(304, Methods = Reads, NotWhenStreaming = true,
    Description = "The caller's copy is current; nothing is sent.")]
[AnswersHeader(304, KnownHeaders.ETag, Methods = Reads, NotWhenStreaming = true,
    Description = "The tag the caller already holds, repeated.")]
[AnswersHeader(200, KnownHeaders.ETag, Methods = Reads, NotWhenStreaming = true,
    Description = "A tag for this response, to send back in If-None-Match.")]
[ReadsHeader(KnownHeaders.IfNoneMatch, Methods = Reads, NotWhenStreaming = true,
    Description = "A tag a previous response carried. Matching it is answered 304.")]
[ReadsHeader(KnownHeaders.IfModifiedSince, Methods = Reads, NotWhenStreaming = true,
    Description = "When the caller last read this. Unchanged since then is answered 304.")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ConditionalGetAttribute : Attribute, IRequestFilterProvider {

    /// <summary>
    /// The methods this answers on, as the document declarations above state it.
    /// </summary>
    /// <remarks>
    /// Spelled once rather than five times, and beside <c>GetFilters</c>'s own check, because a
    /// document claiming a 304 on an operation the filter stood down on is the defect the
    /// declarations were added to close.
    /// </remarks>
    private const string Reads = "GET,HEAD";

    private readonly ConditionalGetFilter _filter = new();

    /// <summary>
    /// One filter instance per declaration, shared by every request, at
    /// <see cref="FilterOrder.Conditional"/>.
    /// </summary>
    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        if (!ConditionalGetFilter.IsGetOrHead(handlerInfo.Method) || handlerInfo.StreamsResponse) {
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
