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
/// no atomic operations, which is what invalidating a group of entries needs.
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
}
