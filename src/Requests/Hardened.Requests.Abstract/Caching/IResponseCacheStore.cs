namespace Hardened.Requests.Abstract.Caching;

/// <summary>
/// Where cached responses are kept.
///
/// <para>
/// Nothing registers one by default. A store is a DI registration, which is exactly what a trimmer
/// cannot remove, so an application that does not cache would otherwise carry the code and the
/// dependency for it. Reference <c>Hardened.Requests.Caching.Memory</c> and write
/// <c>[HardenedMemoryResponseCache]</c>, or register an implementation of this.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <c>IMemoryCache</c> and not <c>IDistributedCache</c>.</b> Microsoft made the same split
/// with <c>IOutputCacheStore</c>, and their reason for it holds here: <c>IDistributedCache</c> has
/// no atomic operations, which is what <see cref="EvictByTag"/> needs.
/// <c>IMemoryCache</c> has that problem too, plus an <c>object</c>-keyed API that boxes on the hot
/// path. Typing against this interface is what lets a Lambda deployment replace one registration
/// with a shared store and change nothing else.
/// </para>
/// <para>
/// Both members take a <see cref="CancellationToken"/> because an implementation over a network
/// store needs one, and the in-process implementation ignoring it costs nothing.
/// </para>
/// </remarks>
public interface IResponseCacheStore {

    /// <summary>
    /// The entry stored under <paramref name="key"/>, or null when there is none or it has expired.
    /// </summary>
    ValueTask<CachedResponse?> Get(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="response"/> under <paramref name="key"/> for
    /// <paramref name="duration"/>.
    /// </summary>
    /// <remarks>
    /// A store may refuse - an entry over its per-item cap, a store already at its limit - and says
    /// so by doing nothing. A refused store is not an error: the response was already written to
    /// the client by the time this is called, and the only consequence is that the next request
    /// misses.
    /// </remarks>
    ValueTask Set(
        string key, CachedResponse response, TimeSpan duration, CancellationToken cancellationToken);

    /// <summary>
    /// Drops every entry stored under <paramref name="tag"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only way an application reaches its own entries, and deliberately the only way. A
    /// <c>Remove(key)</c> would mean composing the key the filter composed - the handler's
    /// "METHOD path", a unit separator, the caller when the scope is per-caller, then each
    /// strategy's part - which is a shape nothing publishes and no adopter should have to
    /// reverse-engineer. A tag is a name the declaration chose.
    /// </para>
    /// <para>
    /// Group invalidation is the reason this interface exists rather than
    /// <c>IDistributedCache</c>, which has no atomic operation to do it with, so this is the member
    /// that argument was about. A store that cannot do it atomically should still do it: entries
    /// dropped one at a time is a worse guarantee than all at once, and both are better than a
    /// publish nobody can see for a minute.
    /// </para>
    /// <para>
    /// A tag nothing was stored under is not an error. An application invalidating what it just
    /// wrote does not know whether anything had read it yet.
    /// </para>
    /// </remarks>
    ValueTask EvictByTag(string tag, CancellationToken cancellationToken);
}
