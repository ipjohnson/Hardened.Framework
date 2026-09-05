using Hardened.Requests.Abstract.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Web.Runtime.Compression;

/// <summary>
/// Compresses this operation's responses, or every operation on this class, under the configured
/// media-type rule.
///
/// <code>
/// [Get("/pets")]
/// [Compress(Favor = CompressionType.Br)]
/// public Task&lt;List&lt;Pet&gt;&gt; List() =&gt; …
/// </code>
///
/// <para>
/// One declaration per operation, on the method or on its class. Both at once is build
/// diagnostic <c>HRDW003</c>, and at run time the inner filter finds the body already wrapped and
/// stands down, so a slip cannot produce two encoders.
/// </para>
/// <para>
/// An operation carrying this is left alone by the application-wide default that
/// <c>[Enable&lt;ResponseCompression&gt;]</c> installs, so the declaration on the operation is the
/// one that applies.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CompressAttribute : Attribute, IRequestFilterProvider {

    /// <summary>
    /// The coding to try first when the client accepts more than one.
    /// </summary>
    /// <remarks>
    /// <see cref="CompressionType.Default"/> follows the configured order. A favoured coding has
    /// to be one the configuration offers; the attribute reorders, it does not enable.
    /// </remarks>
    public CompressionType Favor { get; set; }

    public virtual IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return Filter(new ResponseCompressionFilter(predicate: null, Favor));
    }

    /// <summary>
    /// Whether the handler declares compression itself, in either form, on the method or on its
    /// class. What the application-wide default checks before standing down.
    /// </summary>
    public static bool Declares(IExecutionRequestHandlerInfo handlerInfo) {
        foreach (var item in handlerInfo.Metadata) {
            if (item is CompressAttribute) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One filter instance per handler, shared by every request, at the position both filters
    /// take. The configuration is read from the application's services on the first request.
    /// </summary>
    protected static RequestFilterInfo Filter(ResponseCompressionFilter filter) =>
        new(_ => filter, FilterOrder.Before + FilterOrder.ResponseCache, nameof(ResponseCompressionFilter));
}

/// <summary>
/// Compresses this operation's responses when <typeparamref name="TPredicate"/> says so, given
/// the value the handler returned.
///
/// <code>
/// [Get("/pets")]
/// [Compress&lt;ListLargerThan&gt;(50, Favor = CompressionType.Br)]
/// public Task&lt;List&lt;Pet&gt;&gt; List() =&gt; …
/// </code>
///
/// <para>
/// The arguments reach the predicate through <see cref="ICompressionPredicate.Create"/>, exactly
/// how a cache key provider takes the values from <c>[CacheResponse&lt;VaryByQuery&gt;("page")]</c>.
/// A <c>params</c> parameter has to be last, so anything else on the line is a property set with
/// <c>=</c>, the way <c>[Retry(Retries = 2)]</c> reads.
/// </para>
/// <para>
/// The predicate replaces the media-type rule for this operation, so it can opt in a type the
/// default list leaves out. It is not consulted for a response replayed from the cache, which has
/// no handler value; that follows the default rule.
/// </para>
/// </summary>
/// <typeparam name="TPredicate">
/// The rule. Built once per handler as its filter chain is assembled, so a predicate handed
/// arguments it cannot use fails there, naming the handler, rather than on a request.
/// </typeparam>
public sealed class CompressAttribute<TPredicate> : CompressAttribute
    where TPredicate : ICompressionPredicate {

    public CompressAttribute(params object[] args) {
        Args = args;
    }

    /// <summary>
    /// The predicate's positional arguments, as written on the attribute.
    /// </summary>
    public object[] Args { get; }

    public override IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        ICompressionPredicate predicate;

        try {
            predicate = TPredicate.Create(Args);
        }
        catch (Exception exception) {
            throw new InvalidOperationException(
                $"[Compress<{typeof(TPredicate).Name}>] on {handlerInfo.Method} {handlerInfo.Path} " +
                $"could not build its predicate: {exception.Message}",
                exception);
        }

        yield return Filter(new ResponseCompressionFilter(predicate, Favor));
    }
}
