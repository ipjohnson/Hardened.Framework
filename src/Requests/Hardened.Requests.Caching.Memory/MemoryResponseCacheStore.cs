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
/// </remarks>
public sealed class MemoryResponseCacheStore : IResponseCacheStore, IDisposable {

    private readonly MemoryCache _cache;
    private readonly long _maximumBodySize;

    public MemoryResponseCacheStore(IOptions<IMemoryResponseCacheConfiguration> configuration) {
        var settings = configuration.Value;

        _maximumBodySize = settings.MaximumBodySize;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = settings.SizeLimit });
    }

    public ValueTask<CachedResponse?> Get(string key, CancellationToken cancellationToken) =>
        new(_cache.TryGetValue(key, out CachedResponse? cached) ? cached : null);

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

        _cache.Set(key, response, new MemoryCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = duration,

            // Sized, because MemoryCacheOptions.SizeLimit is only enforced when every entry says
            // how big it is - and an entry with no size on a cache with a limit throws.
            Size = response.Size
        });

        return default;
    }

    public void Dispose() => _cache.Dispose();
}
