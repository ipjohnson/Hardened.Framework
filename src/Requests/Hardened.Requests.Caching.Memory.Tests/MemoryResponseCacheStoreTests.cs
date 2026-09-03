using Hardened.Requests.Abstract.Caching;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Caching.Memory.Tests;

/// <summary>
/// What the in-process store keeps, what it refuses, and what it hands back.
/// </summary>
public class MemoryResponseCacheStoreTests {

    private static MemoryResponseCacheStore Store(
        long sizeLimit = MemoryResponseCacheConfiguration.DefaultSizeLimit,
        long maximumBodySize = MemoryResponseCacheConfiguration.DefaultMaximumBodySize) =>
        new(Options.Create<IMemoryResponseCacheConfiguration>(
            new MemoryResponseCacheConfiguration {
                SizeLimit = sizeLimit,
                MaximumBodySize = maximumBodySize
            }));

    private static CachedResponse Response(
        int bodyLength = 4,
        string? contentType = "application/json",
        params string[] tags) =>
        new(200, contentType, new byte[bodyLength], [
            new KeyValuePair<string, StringValues>("Cache-Control", new StringValues("public"))
        ], tags);

    [Fact]
    public async Task AKeyNothingWasStoredUnderReadsBackAsNothing() {
        using var store = Store();

        Assert.Null(await store.Get("absent", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhatWasStoredIsWhatComesBack() {
        using var store = Store();
        var stored = Response();

        await store.Set("k", stored, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        var read = await store.Get("k", TestContext.Current.CancellationToken);

        Assert.Same(stored, read);
    }

    /// <summary>
    /// One large response is how a total size limit gets spent on something nothing hits again, so
    /// there is a per-entry cap as well as a total.
    /// </summary>
    [Fact]
    public async Task AResponseOverThePerEntryCapIsNotStored() {
        using var store = Store(maximumBodySize: 8);

        await store.Set(
            "k", Response(bodyLength: 9), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AResponseAtThePerEntryCapIsStored() {
        using var store = Store(maximumBodySize: 8);

        await store.Set(
            "k", Response(bodyLength: 8), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// <c>TryGetValue</c> checks expiry on read, which is what keeps the store correct across a
    /// Lambda freeze of any length: nothing has to fire for a stale entry to be withheld.
    /// </summary>
    [Fact]
    public async Task AnExpiredEntryIsNotReturned() {
        using var store = Store();

        await store.Set(
            "k", Response(), TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASecondStoreUnderOneKeyReplacesTheFirst() {
        using var store = Store();
        var second = Response(bodyLength: 8);

        await store.Set(
            "k", Response(bodyLength: 4), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        await store.Set("k", second, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Same(second, await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A store with a limit throws on an entry that does not say how big it is, so every entry
    /// carries its size. This is what proves it does.
    /// </summary>
    [Fact]
    public async Task EveryEntryIsSizedAgainstTheLimit() {
        using var store = Store(sizeLimit: 16);

        await store.Set(
            "k", Response(bodyLength: 4), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The bytes are what an entry costs. Header names and values are bounded and small beside a
    /// body, and a size that walked them would be paid on every store for a correction below the
    /// noise.
    /// </summary>
    [Fact]
    public void AnEntryCostsItsBody() {
        Assert.Equal(4, Response(bodyLength: 4).Size);
    }

    /// <summary>
    /// The seam the 0.19.0-rc1000 trial found missing: an application could not reach its own
    /// entries at all, so a published change appeared when the entry expired and not before.
    /// </summary>
    [Fact]
    public async Task AnEntryIsGoneOnceItsTagIsEvicted() {
        using var store = Store();

        await store.Set(
            "k",
            Response(tags: "rates"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Every entry under the tag, which is the whole point: one publish invalidates the read of
    /// each symbol rather than the one the publisher happened to name.
    /// </summary>
    [Fact]
    public async Task EveryEntryUnderTheTagGoes() {
        using var store = Store();

        foreach (var key in new[] { "EUR", "GBP", "JPY" }) {
            await store.Set(
                key,
                Response(tags: "rates"),
                TimeSpan.FromHours(1),
                TestContext.Current.CancellationToken);
        }

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("EUR", TestContext.Current.CancellationToken));
        Assert.Null(await store.Get("GBP", TestContext.Current.CancellationToken));
        Assert.Null(await store.Get("JPY", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEntryUnderAnotherTagStays() {
        using var store = Store();

        await store.Set(
            "rate", Response(tags: "rates"), TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await store.Set(
            "alert", Response(tags: "alerts"), TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("alert", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An entry carrying two tags is reachable by either, and evicting one takes it out of the
    /// other's index rather than leaving a key nothing can serve.
    /// </summary>
    [Fact]
    public async Task AnEntryUnderTwoTagsGoesWithTheFirstOfThem() {
        using var store = Store();

        await store.Set(
            "k",
            Response(tags: ["rates", "symbols"]),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));

        // The second tag no longer names it, so re-storing under that tag and evicting the first
        // does not take the new entry with it.
        await store.Set(
            "k",
            Response(tags: "symbols"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A key written again under a different tag is reachable by the new one and not by the old.
    /// </summary>
    /// <remarks>
    /// The index is keyed by tag, so a replacement has to be unindexed as it is replaced. Left in,
    /// the old tag would drop an entry that is no longer tagged that way.
    /// </remarks>
    [Fact]
    public async Task AReplacedEntryIsIndexedByItsNewTag() {
        using var store = Store();

        await store.Set(
            "k", Response(tags: "rates"), TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await store.Set(
            "k", Response(tags: "alerts"), TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));

        await store.EvictByTag("alerts", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A key stored again after its tag was evicted is still reachable by that tag.
    /// </summary>
    /// <remarks>
    /// MemoryCache raises eviction callbacks on the thread pool, so the callback cleaning the index
    /// for the removed entry can arrive after the replacement has been indexed. Acting on it then
    /// leaves an entry nothing can invalidate, which is a publish nobody sees until the duration
    /// runs out - the defect the tag was added for, back again as a race.
    /// </remarks>
    [Fact]
    public async Task AKeyStoredAgainAfterAnEvictionIsStillReachableByItsTag() {
        using var store = Store();

        await store.Set(
            "k", Response(tags: "rates"), TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        await store.Set(
            "k", Response(tags: "rates"), TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A tag nothing was stored under is not an error. An application invalidating what it just
    /// wrote does not know whether anything had read it yet.
    /// </summary>
    [Fact]
    public async Task EvictingATagNothingUsedDoesNothing() {
        using var store = Store();

        await store.Set("k", Response(), TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await store.EvictByTag("nothing", TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }
}
