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

    private static CachedResponse Response(int bodyLength = 4, string? contentType = "application/json") =>
        new(200, contentType, new byte[bodyLength], [
            new KeyValuePair<string, StringValues>("Cache-Control", new StringValues("public"))
        ]);

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
}
