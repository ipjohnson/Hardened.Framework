using Hardened.Requests.Abstract.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Caching.Memory;

/// <summary>
/// An <see cref="IResponseCacheStore"/> over <see cref="MemoryCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns a <see cref="MemoryCache"/> rather than taking an <c>IMemoryCache</c>.</b> Microsoft
/// made the same split with <c>IOutputCacheStore</c>. A shared <c>IMemoryCache</c> is keyed on
/// <c>object</c>, so every lookup boxes; its entries are sized in whatever unit the application
/// chose, so a size limit here would be enforced in units this cannot know; and an application that
/// registered no <c>IMemoryCache</c> would get one registered on its behalf, holding response
/// bodies, for having referenced a package.
/// </para>
/// <para>
/// <b>Two properties of <see cref="MemoryCache"/> in .NET 8 are what make it right on Lambda.</b>
/// Expiry is not timer-driven - <c>StartScanForExpiredItemsIfNeeded</c> is called from
/// <c>TryGetValue</c>, <c>SetEntry</c> and <c>Remove</c>, and schedules its scan on the thread pool
/// - so a frozen execution environment does not stop it working. And <c>TryGetValue</c> checks
/// expiry on read, so an entry that went stale during a freeze of any length is never returned.
/// Only reclamation happens late.
/// </para>
/// <para>
/// <b>Whether an entry is still valid is decided on the <see cref="TimeProvider"/> this was
/// given, not on the one <see cref="MemoryCache"/> reclaims with.</b> Nothing in the response-cache
/// contract took a clock, so a test for a five-minute entry could only sleep for five minutes and
/// a test for the specification's day-long entry could not be written at all. Substituting the
/// whole store was the workaround every trial arm reached for. The absolute expiration is still
/// set, because that is what frees the memory without a read; the check on the way out is what
/// makes the duration mean something a test can move.
/// </para>
/// </remarks>
public sealed class MemoryResponseCacheStore : IResponseCacheStore, IDisposable {

    private readonly MemoryCache _cache;
    private readonly long _maximumBodySize;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Which keys each tag names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Held here rather than asked of <see cref="MemoryCache"/>, which cannot enumerate its own
    /// keys and could not answer "every entry tagged rates" if it could - the tag is on the entry.
    /// </para>
    /// <para>
    /// Under a lock rather than concurrent collections. Every operation on it is a whole tag or a
    /// whole entry, so the unit that has to be atomic is bigger than one dictionary write, and
    /// <see cref="EvictByTag"/> taking the tag out before touching the cache is what stops a
    /// concurrent store from re-indexing a key mid-eviction.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, HashSet<string>> _keysByTag = new(StringComparer.Ordinal);

    public MemoryResponseCacheStore(
        IOptions<IMemoryResponseCacheConfiguration> configuration, TimeProvider timeProvider) {
        var settings = configuration.Value;

        _maximumBodySize = settings.MaximumBodySize;
        _timeProvider = timeProvider;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = settings.SizeLimit });
    }

    /// <summary>
    /// The entry stored under <paramref name="key"/>, or null when there is none or it has expired.
    /// </summary>
    /// <remarks>
    /// An entry past its duration is removed rather than merely withheld, so the tag index does not
    /// keep naming a response nothing will be served.
    /// </remarks>
    public ValueTask<CachedResponse?> Get(string key, CancellationToken cancellationToken) {
        if (!_cache.TryGetValue(key, out Entry? entry) || entry == null) {
            return new ValueTask<CachedResponse?>((CachedResponse?)null);
        }

        if (entry.ExpiresAt > _timeProvider.GetUtcNow()) {
            return new ValueTask<CachedResponse?>(entry.Response);
        }

        // Unindexed here rather than left to the eviction callback, which arrives on the thread
        // pool: a tag still naming this key would take the next entry stored under the key with it
        // the next time that tag was evicted.
        lock (_keysByTag) {
            Forget(key, entry);

            _cache.Remove(key);
        }

        return new ValueTask<CachedResponse?>((CachedResponse?)null);
    }

    /// <summary>
    /// Stores <paramref name="response"/>, unless it is larger than one entry may be.
    /// </summary>
    /// <remarks>
    /// Refusing by doing nothing, which is what the contract asks for. The response was written to
    /// the client before this was called, so there is nothing to fail: the only consequence of a
    /// refusal is that the next request misses.
    /// </remarks>
    public ValueTask Set(
        string key, CachedResponse response, TimeSpan duration, CancellationToken cancellationToken) {
        if (response.Size > _maximumBodySize) {
            return default;
        }

        // Written and indexed as one step, under the lock the eviction callback also takes. Apart,
        // a callback for the entry being replaced can arrive after the new entry has been indexed -
        // MemoryCache raises them on the thread pool - and unindex the entry that just arrived,
        // which leaves a response nothing can invalidate and a publish nobody sees until it
        // expires.
        lock (_keysByTag) {
            // A key written again may have been indexed under different tags. Unindexing what is
            // being replaced is what keeps EvictByTag from dropping an entry that is no longer
            // tagged that way, and it is why the callback ignores a replacement.
            if (_cache.TryGetValue(key, out Entry? replaced)) {
                Forget(key, replaced);
            }

            _cache.Set(key, new Entry(response, _timeProvider.GetUtcNow() + duration), new MemoryCacheEntryOptions {
                AbsoluteExpirationRelativeToNow = duration,

                // Sized, because MemoryCacheOptions.SizeLimit is only enforced when every entry says
                // how big it is - and an entry with no size on a cache with a limit throws.
                Size = response.Size
            }.RegisterPostEvictionCallback(OnEvicted));

            Index(key, response.Tags);
        }

        return default;
    }

    /// <summary>
    /// Drops every entry stored under <paramref name="tag"/>.
    /// </summary>
    /// <remarks>
    /// The tag is taken out of the index before anything is removed from the cache, so a concurrent
    /// store cannot add a key to a tag that is halfway through being evicted. Keys carrying other
    /// tags as well are unindexed from those by the eviction callback.
    /// </remarks>
    public ValueTask EvictByTag(string tag, CancellationToken cancellationToken) {
        string[] keys;

        lock (_keysByTag) {
            if (!_keysByTag.Remove(tag, out var tagged)) {
                return default;
            }

            keys = [..tagged];
        }

        foreach (var key in keys) {
            _cache.Remove(key);
        }

        return default;
    }

    public void Dispose() => _cache.Dispose();

    /// <summary>
    /// A stored response and when this store stops answering with it.
    /// </summary>
    /// <remarks>
    /// The expiry is held rather than derived, so the entry carries the answer for the clock the
    /// store was given. <see cref="MemoryCache"/> holds its own, on the machine clock, and that one
    /// only decides when the memory is freed.
    /// </remarks>
    private sealed record Entry(CachedResponse Response, DateTimeOffset ExpiresAt);

    /// <remarks>Called holding the lock.</remarks>
    private void Index(string key, IReadOnlyList<string> tags) {
        foreach (var tag in tags) {
            if (!_keysByTag.TryGetValue(tag, out var tagged)) {
                _keysByTag[tag] = tagged = new HashSet<string>(StringComparer.Ordinal);
            }

            tagged.Add(key);
        }
    }

    /// <summary>
    /// Takes an entry that is no longer in the cache out of the index.
    /// </summary>
    /// <remarks>
    /// Without it the index grows for the life of the process: every expired entry would leave its
    /// key behind under every tag it carried, and an eviction by tag would walk keys that are long
    /// gone.
    /// </remarks>
    private void OnEvicted(object key, object? value, EvictionReason reason, object? state) {
        // A replacement has already been unindexed by Set, which knew the new entry's tags.
        if (reason == EvictionReason.Replaced) {
            return;
        }

        lock (_keysByTag) {
            // The index entry belongs to whatever is in the cache now, which may not be what was
            // evicted: these arrive on the thread pool, so a key can be stored again before its
            // predecessor's callback runs. Unindexing then would leave a response nothing can
            // invalidate.
            if (_cache.TryGetValue(key, out Entry? current) && !ReferenceEquals(current, value)) {
                return;
            }

            Forget(key, value);
        }
    }

    /// <remarks>Called holding the lock.</remarks>
    private void Forget(object key, object? value) {
        if (value is not Entry entry) {
            return;
        }

        var name = (string)key;

        foreach (var tag in entry.Response.Tags) {
            if (!_keysByTag.TryGetValue(tag, out var tagged)) {
                continue;
            }

            tagged.Remove(name);

            if (tagged.Count == 0) {
                _keysByTag.Remove(tag);
            }
        }
    }
}
