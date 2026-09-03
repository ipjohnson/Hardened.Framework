using System.Diagnostics.CodeAnalysis;
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
            // Not null when the lookup succeeded: this store is the only thing that writes to its
            // own cache, and it never writes one.
            if (_cache.TryGetValue(key, out Entry? replaced)) {
                Forget(key, replaced!);
            }

            var entry = new Entry(response, _timeProvider.GetUtcNow() + duration) {
                TagSets = Index(key, response.Tags)
            };

            _cache.Set(key, entry, new MemoryCacheEntryOptions {
                AbsoluteExpirationRelativeToNow = duration,

                // Sized, because MemoryCacheOptions.SizeLimit is only enforced when every entry says
                // how big it is - and an entry with no size on a cache with a limit throws.
                Size = response.Size
            }.RegisterPostEvictionCallback(OnEvicted));
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
    private sealed record Entry(CachedResponse Response, DateTimeOffset ExpiresAt) {

        /// <summary>
        /// The index sets this entry was added to, so leaving them needs no lookup. Empty for an
        /// entry whose declaration named no tags, which is most of them.
        /// </summary>
        public IReadOnlyList<(string Tag, HashSet<string> Keys)> TagSets { get; init; } = [];
    }

    /// <summary>
    /// Adds <paramref name="key"/> to each tag's keys, and hands back the sets it joined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sets rather than the tag names, because the entry keeps them: leaving an index is then a
    /// removal from a set the entry already holds, with no dictionary lookup that could miss. The
    /// case that would have missed is real - a tag evicted while an entry carrying it is still in
    /// the cache - and it arrives on the thread pool, where no test can meet it.
    /// </para>
    /// <para>Called holding the lock.</para>
    /// </remarks>
    private IReadOnlyList<(string Tag, HashSet<string> Keys)> Index(
        string key, IReadOnlyList<string> tags) {
        if (tags.Count == 0) {
            return [];
        }

        var joined = new (string, HashSet<string>)[tags.Count];

        for (var i = 0; i < tags.Count; i++) {
            var tag = tags[i];

            if (!_keysByTag.TryGetValue(tag, out var tagged)) {
                _keysByTag[tag] = tagged = new HashSet<string>(StringComparer.Ordinal);
            }

            tagged.Add(key);
            joined[i] = (tag, tagged);
        }

        return joined;
    }

    /// <summary>
    /// Takes an entry <see cref="MemoryCache"/> reclaimed out of the index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The only cleanup this store does not schedule itself.</b> <c>Set</c>, <c>Get</c> and
    /// <see cref="EvictByTag"/> each unindex before they remove, so by the time this runs for one
    /// of them there is nothing left to do. What is left is what <see cref="MemoryCache"/>
    /// reclaims on its own - an expiry its own scan found, a compaction under the size limit - and
    /// without this the index would name those keys for the life of the process.
    /// </para>
    /// <para>
    /// Excluded from coverage rather than asserted. <see cref="MemoryCache"/> raises these on the
    /// thread pool, so whether it has run at any moment is a race, and no sequence of <c>Get</c>,
    /// <c>Set</c> and <see cref="EvictByTag"/> can both schedule one and observe its effect: the
    /// index is not readable from outside, and every consequence of it is one the synchronous
    /// paths have already produced. A test here would assert a timing window.
    /// </para>
    /// </remarks>
    [ExcludeFromCodeCoverage(
        Justification = "Raised by MemoryCache on the thread pool, for reclamation this store did " +
                        "not schedule. No test driving the public surface can execute it.")]
    private void OnEvicted(object key, object? value, EvictionReason reason, object? state) {
        // A replacement has already been unindexed by Set, which knew the new entry's tags.
        if (reason == EvictionReason.Replaced || value is not Entry evicted) {
            return;
        }

        lock (_keysByTag) {
            // The index entry belongs to whatever is in the cache now, which may not be what was
            // evicted: these arrive on the thread pool, so a key can be stored again before its
            // predecessor's callback runs. Unindexing then would leave a response nothing can
            // invalidate.
            if (_cache.TryGetValue(key, out Entry? current) && !ReferenceEquals(current, evicted)) {
                return;
            }

            Forget((string)key, evicted);
        }
    }

    /// <summary>
    /// Takes an entry that is no longer in the cache out of the sets it joined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without it the index grows for the life of the process: every entry would leave its key
    /// behind under every tag it carried, and an eviction by tag would walk keys that are long
    /// gone.
    /// </para>
    /// <para>
    /// A set emptied this way is left in place. Removing it would have to prove the dictionary
    /// still holds this set rather than one built for the same tag since - and an empty set costs
    /// one entry per tag an application uses, which is a number the application writes by hand.
    /// </para>
    /// <para>Called holding the lock.</para>
    /// </remarks>
    private static void Forget(string key, Entry entry) {
        foreach (var (_, tagged) in entry.TagSets) {
            tagged.Remove(key);
        }
    }
}
