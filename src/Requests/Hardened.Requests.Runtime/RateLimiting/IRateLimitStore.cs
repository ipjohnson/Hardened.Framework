namespace Hardened.Requests.Runtime.RateLimiting;

/// <summary>
/// How a partition's allowance is counted.
/// </summary>
/// <param name="PermitLimit">Requests allowed per <paramref name="Window"/>.</param>
/// <param name="Window">The period the limit applies over.</param>
/// <param name="Name">
/// Which allowance this is, so two policies on the same partition key do not share a counter.
/// </param>
public readonly record struct RateLimitPolicy(int PermitLimit, TimeSpan Window, string Name = "default");

/// <summary>
/// What a store said about one request.
/// </summary>
/// <param name="Allowed">Whether the request may proceed.</param>
/// <param name="Limit">The allowance, for the <c>RateLimit-Limit</c> header.</param>
/// <param name="Remaining">What is left of it.</param>
/// <param name="RetryAfter">
/// How long until the caller should try again. Carried on the decision rather than recomputed by
/// the caller, because a distributed store is the only thing that knows - and a second round trip
/// to ask would double the cost of the request that was just refused.
/// </param>
public readonly record struct RateLimitDecision(
    bool Allowed, int Limit, int Remaining, TimeSpan RetryAfter) {

    public static RateLimitDecision Allow(int limit, int remaining) =>
        new(true, limit, remaining, TimeSpan.Zero);

    public static RateLimitDecision Refuse(int limit, TimeSpan retryAfter) =>
        new(false, limit, 0, retryAfter);
}

/// <summary>
/// Where the counting happens.
/// </summary>
/// <remarks>
/// <para>
/// <b>One method, deliberately.</b> This is a primitive, not a data-access layer: it says whether
/// this request fits in this allowance and nothing else. Everything a real implementation needs -
/// eviction, clock skew, single-flight refresh, what to do when the backing store is unreachable -
/// is a property of that implementation's strategy, and putting any of it in the contract would
/// commit every implementation to one answer.
/// </para>
/// <para>
/// <b>Replacing it is the whole point.</b> The shipped implementation counts in process, which on
/// more than one instance means each counts separately - and on Lambda means each execution
/// environment counts separately, where the number of them is exactly what you do not control.
/// An application that needs one shared count implements this against whatever it already runs:
/// </para>
/// <code>
/// [SingletonService(Using = RegistrationType.Replace)]
/// public class RedisRateLimitStore : IRateLimitStore {
///     public RedisRateLimitStore(IConnectionMultiplexer redis) { }
///
///     public ValueTask&lt;RateLimitDecision&gt; Acquire(
///         string partition, RateLimitPolicy policy, CancellationToken cancellationToken) { }
/// }
/// </code>
/// <para>
/// No registration call and no module wiring: the default is registered with
/// <c>RegistrationType.Try</c>, so an application's own registration wins whichever order the
/// modules load in. <c>Replace</c> rather than the default <c>Add</c> because both resolve to the
/// application's implementation but <c>Add</c> leaves the framework's descriptor registered too,
/// and it would still be constructed by anything enumerating the service.
/// </para>
/// <para>
/// Nothing cloud-specific ships here. A store backed by Redis, DynamoDB or anything else is a
/// dependency on that thing, and the framework does not need one to define the seam.
/// </para>
/// </remarks>
public interface IRateLimitStore {

    /// <summary>
    /// Takes one permit from <paramref name="partition"/>'s allowance, if there is one.
    /// </summary>
    /// <param name="partition">
    /// Who is being limited - see <see cref="IRateLimitPartitioner"/>. Opaque to the store.
    /// </param>
    ValueTask<RateLimitDecision> Acquire(
        string partition, RateLimitPolicy policy, CancellationToken cancellationToken);
}
