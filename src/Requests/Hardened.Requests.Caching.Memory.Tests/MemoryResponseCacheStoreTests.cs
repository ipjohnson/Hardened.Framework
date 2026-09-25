using System.Globalization;
using Hardened.Requests.Abstract.Caching;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Caching.Memory.Tests;

/// <summary>
/// What the in-process store keeps, what it refuses, and what it hands back.
/// </summary>
public class MemoryResponseCacheStoreTests
{
    private static MemoryResponseCacheStore Store(
        long sizeLimit = MemoryResponseCacheConfiguration.DefaultSizeLimit,
        long maximumBodySize = MemoryResponseCacheConfiguration.DefaultMaximumBodySize,
        TimeProvider? clock = null
    ) =>
        new(
            Options.Create<IMemoryResponseCacheConfiguration>(
                new MemoryResponseCacheConfiguration
                {
                    SizeLimit = sizeLimit,
                    MaximumBodySize = maximumBodySize,
                }
            ),
            clock ?? TimeProvider.System
        );

    private static CachedResponse Response(
        int bodyLength = 4,
        string? contentType = "application/json",
        params string[] tags
    ) =>
        new(
            200,
            contentType,
            new byte[bodyLength],
            [new KeyValuePair<string, StringValues>("Cache-Control", new StringValues("public"))],
            tags
        );

    [Fact]
    public async Task AKeyNothingWasStoredUnderReadsBackAsNothing()
    {
        using var store = Store();

        Assert.Null(await store.Get("absent", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhatWasStoredIsWhatComesBack()
    {
        using var store = Store();
        var stored = Response();

        await store.Set(
            "k",
            stored,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );

        var read = await store.Get("k", TestContext.Current.CancellationToken);

        Assert.Same(stored, read);
    }

    /// <summary>
    /// One large response is how a total size limit gets spent on something nothing hits again, so
    /// there is a per-entry cap as well as a total.
    /// </summary>
    [Fact]
    public async Task AResponseOverThePerEntryCapIsNotStored()
    {
        using var store = Store(maximumBodySize: 8);

        await store.Set(
            "k",
            Response(bodyLength: 9),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AResponseAtThePerEntryCapIsStored()
    {
        using var store = Store(maximumBodySize: 8);

        await store.Set(
            "k",
            Response(bodyLength: 8),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// <c>TryGetValue</c> checks expiry on read, which is what keeps the store correct across a
    /// Lambda freeze of any length: nothing has to fire for a stale entry to be withheld.
    /// </summary>
    [Fact]
    public async Task AnExpiredEntryIsNotReturned()
    {
        using var store = Store();

        await store.Set(
            "k",
            Response(),
            TimeSpan.FromMilliseconds(1),
            TestContext.Current.CancellationToken
        );

        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASecondStoreUnderOneKeyReplacesTheFirst()
    {
        using var store = Store();
        var second = Response(bodyLength: 8);

        await store.Set(
            "k",
            Response(bodyLength: 4),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );
        await store.Set(
            "k",
            second,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );

        Assert.Same(second, await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A store with a limit throws on an entry that does not say how big it is, so every entry
    /// carries its size. This is what proves it does.
    /// </summary>
    [Fact]
    public async Task EveryEntryIsSizedAgainstTheLimit()
    {
        using var store = Store(sizeLimit: 1024);

        await store.Set(
            "k",
            Response(bodyLength: 4),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An entry costs its body, and its content type, headers and tags at two bytes a character:
    /// 4 + (16 + 13 + 6) × 2 for <c>application/json</c> and <c>Cache-Control: public</c>.
    /// </summary>
    [Fact]
    public void AnEntryCostsItsBodyAndItsStrings()
    {
        Assert.Equal(74, Response(bodyLength: 4).Size);
        Assert.Equal(84, Response(bodyLength: 4, tags: "rates").Size);
    }

    /// <summary>
    /// The key counts too, because it carries the request's values. A 10,000-character key is
    /// 20,000 bytes, which a 4,096-byte store has no room for.
    /// </summary>
    [Fact]
    public async Task AKeyCountsAgainstTheLimit()
    {
        using var store = Store(sizeLimit: 4096);
        var longKey = new string('q', 10_000);

        await store.Set(
            "k",
            Response(),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );
        await store.Set(
            longKey,
            Response(),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
        Assert.Null(await store.Get(longKey, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A caller varying a query value on every request, with a two-byte body each time. Counted by
    /// bodies alone, all 10,000 entries fit in 64 KB. Each entry also costs a 512-byte allowance, so
    /// no more than 128 of them can.
    /// </summary>
    [Fact]
    public async Task ManySmallEntriesStayInsideTheLimit()
    {
        using var store = Store(sizeLimit: 64 * 1024);
        var keys = Enumerable
            .Range(0, 10_000)
            .Select(i =>
                "GET /catalog\u001fculture=" + i.ToString("D40", CultureInfo.InvariantCulture) + "&"
            )
            .ToArray();

        foreach (var key in keys)
        {
            await store.Set(
                key,
                Response(bodyLength: 2),
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken
            );
        }

        var stored = 0;

        foreach (var key in keys)
        {
            if (await store.Get(key, TestContext.Current.CancellationToken) != null)
            {
                stored++;
            }
        }

        Assert.InRange(stored, 1, 64 * 1024 / 512);
    }

    /// <summary>
    /// The seam the 0.19.0-rc1000 trial found missing: an application could not reach its own
    /// entries at all, so a published change appeared when the entry expired and not before.
    /// </summary>
    [Fact]
    public async Task AnEntryIsGoneOnceItsTagIsEvicted()
    {
        using var store = Store();

        await store.Set(
            "k",
            Response(tags: "rates"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Every entry under the tag, which is the whole point: one publish invalidates the read of
    /// each symbol rather than the one the publisher happened to name.
    /// </summary>
    [Fact]
    public async Task EveryEntryUnderTheTagGoes()
    {
        using var store = Store();

        foreach (var key in new[] { "EUR", "GBP", "JPY" })
        {
            await store.Set(
                key,
                Response(tags: "rates"),
                TimeSpan.FromHours(1),
                TestContext.Current.CancellationToken
            );
        }

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("EUR", TestContext.Current.CancellationToken));
        Assert.Null(await store.Get("GBP", TestContext.Current.CancellationToken));
        Assert.Null(await store.Get("JPY", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEntryUnderAnotherTagStays()
    {
        using var store = Store();

        await store.Set(
            "rate",
            Response(tags: "rates"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

        await store.Set(
            "alert",
            Response(tags: "alerts"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("alert", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An entry carrying two tags is reachable by either, and evicting one takes it out of the
    /// other's index rather than leaving a key nothing can serve.
    /// </summary>
    [Fact]
    public async Task AnEntryUnderTwoTagsGoesWithTheFirstOfThem()
    {
        using var store = Store();

        await store.Set(
            "k",
            Response(tags: ["rates", "symbols"]),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));

        // The second tag no longer names it, so re-storing under that tag and evicting the first
        // does not take the new entry with it.
        await store.Set(
            "k",
            Response(tags: "symbols"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

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
    public async Task AReplacedEntryIsIndexedByItsNewTag()
    {
        using var store = Store();

        await store.Set(
            "k",
            Response(tags: "rates"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

        await store.Set(
            "k",
            Response(tags: "alerts"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

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
    public async Task AKeyStoredAgainAfterAnEvictionIsStillReachableByItsTag()
    {
        using var store = Store();

        await store.Set(
            "k",
            Response(tags: "rates"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        await store.Set(
            "k",
            Response(tags: "rates"),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A tag nothing was stored under is not an error. An application invalidating what it just
    /// wrote does not know whether anything had read it yet.
    /// </summary>
    [Fact]
    public async Task EvictingATagNothingUsedDoesNothing()
    {
        using var store = Store();

        await store.Set(
            "k",
            Response(),
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken
        );

        await store.EvictByTag("nothing", TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The duration is decided on the clock the store was given, so a test can move time instead of
    /// waiting.
    /// </summary>
    /// <remarks>
    /// Nothing in the response-cache contract took a clock, so a test for a five-minute entry could
    /// only sleep and a test for the specification's day-long entry could not be written. Every
    /// trial arm substituted the whole store to get past it.
    /// </remarks>
    [Fact]
    public async Task AnEntryIsGoneOnceTheClockPassesItsDuration()
    {
        var clock = new TestClock();
        using var store = Store(clock: clock);

        await store.Set(
            "k",
            Response(),
            TimeSpan.FromDays(1),
            TestContext.Current.CancellationToken
        );

        clock.Advance(TimeSpan.FromDays(1));

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEntryInsideItsDurationIsStillServed()
    {
        var clock = new TestClock();
        using var store = Store(clock: clock);

        await store.Set(
            "k",
            Response(),
            TimeSpan.FromDays(1),
            TestContext.Current.CancellationToken
        );

        clock.Advance(TimeSpan.FromHours(23));

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An expired entry is dropped rather than withheld, so the tag index stops naming a response
    /// nothing will be served.
    /// </summary>
    [Fact]
    public async Task AnExpiredEntryIsNotEvictedAgainByItsTag()
    {
        var clock = new TestClock();
        using var store = Store(clock: clock);

        await store.Set(
            "k",
            Response(tags: "rates"),
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken
        );

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(await store.Get("k", TestContext.Current.CancellationToken));

        await store.Set(
            "k",
            Response(tags: "alerts"),
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken
        );

        await store.EvictByTag("rates", TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Get("k", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A clock a test moves by hand. Written here rather than taken from
    /// Microsoft.Extensions.TimeProvider.Testing, which would be a package reference for four
    /// lines.
    /// </summary>
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
