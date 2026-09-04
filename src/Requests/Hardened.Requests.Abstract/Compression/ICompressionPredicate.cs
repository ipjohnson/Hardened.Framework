using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Compression;

/// <summary>
/// Whether one operation's response is worth compressing, decided from the value the handler
/// returned.
///
/// <code>
/// public sealed class ListLargerThan : ICompressionPredicate {
///     private readonly int _count;
///
///     private ListLargerThan(int count) => _count = count;
///
///     public static ICompressionPredicate Create(object[] args) => args is [int count]
///         ? new ListLargerThan(count)
///         : throw new ArgumentException("ListLargerThan takes one integer, the count above which the body is compressed.");
///
///     public bool ShouldCompress(object value, IExecutionContext context) =>
///         value is System.Collections.ICollection { Count: var n } &amp;&amp; n > _count;
/// }
/// </code>
///
/// <para>
/// The same shape as <see cref="Caching.ICacheKeyProvider"/>: a static factory the attribute's
/// generic constraint reaches without reflection, and an instance method the filter calls per
/// request. The type argument on <c>[Compress&lt;T&gt;]</c> is checked by the compiler, so a
/// predicate is a type rather than a name resolved at run time.
/// </para>
/// </summary>
public interface ICompressionPredicate {
    /// <summary>
    /// Builds the predicate from the attribute's positional arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>object[]</c> rather than the cache provider's <c>string[]</c>, so a count written as
    /// <c>50</c> arrives as an integer. Anything C# admits as an attribute argument can be here:
    /// numbers, strings, booleans, enums, types and arrays of those.
    /// </para>
    /// <para>
    /// Called once per handler as its filter chain is built, so the instance is shared by every
    /// request and must not hold per-request state. This is where arity is checked: a mismatch
    /// throws here, naming the handler, rather than on a request.
    /// </para>
    /// </remarks>
    static abstract ICompressionPredicate Create(object[] args);

    /// <summary>
    /// Whether the response carrying <paramref name="value"/> is compressed.
    /// </summary>
    /// <remarks>
    /// Replaces the configured media-type rule for the operation, so it can also opt in a type the
    /// default list leaves out. It is asked at the first write of the body and only when the
    /// response carries a handler value: a response replayed from the cache has none, and is
    /// decided by the media-type rule instead.
    /// </remarks>
    bool ShouldCompress(object value, IExecutionContext context);
}
