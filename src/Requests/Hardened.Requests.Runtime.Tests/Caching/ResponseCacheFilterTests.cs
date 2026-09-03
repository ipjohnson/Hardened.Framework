using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Caching;

/// <summary>
/// What the filter does with a hit, with a miss, and with a request no strategy will key.
/// </summary>
public class ResponseCacheFilterTests {

    /// <summary>The unit separator the filter joins composite keys with.</summary>
    private const string Separator = "\u001f";

    private static ResponseCacheFilter Filter(
        int duration = 0, params ICacheKeyProvider[] providers) =>
        new(providers.Length == 0 ? [new CacheTestSupport.FixedKey()] : providers,
            "GET /catalog",
            duration);

    private static IExecutionContext Context(CacheTestSupport.RecordingStore store) =>
        Pipeline.Context(
            configureServices: services => services.AddSingleton<IResponseCacheStore>(store));

    private static string BodyOf(IExecutionContext context) {
        var body = (MemoryStream)context.Response.Body;

        return Encoding.UTF8.GetString(body.ToArray());
    }

    /// <summary>
    /// Writes what the handler wrote, and keeps a copy.
    /// </summary>
    [Fact]
    public async Task AMissRunsTheChainAndStoresWhatItProduced() {
        var store = new CacheTestSupport.RecordingStore();
        var context = Context(store);

        var chain = Pipeline.Chain(context,
            Filter(),
            Writing("catalog"));

        await chain.Next();

        Assert.Equal("catalog", BodyOf(context));
        Assert.Equal("GET /catalog" + Separator + "fixed", Assert.Single(store.Writes).Key);
    }

    /// <summary>
    /// The handler does not run at all, which is the point of sitting ahead of the IO filter rather
    /// than behind it.
    /// </summary>
    [Fact]
    public async Task AHitAnswersWithoutRunningTheChain() {
        var store = new CacheTestSupport.RecordingStore();
        var ran = 0;

        var first = Context(store);

        await Pipeline.Chain(first, Filter(), Writing("catalog", () => ran++)).Next();

        var second = Context(store);

        await Pipeline.Chain(second, Filter(), Writing("catalog", () => ran++)).Next();

        Assert.Equal(1, ran);
        Assert.Equal("catalog", BodyOf(second));
    }

    /// <summary>
    /// A stored response carries its status and content type back, not only its bytes.
    /// </summary>
    [Fact]
    public async Task AHitReplaysTheStatusAndContentType() {
        var store = new CacheTestSupport.RecordingStore();

        await Pipeline.Chain(Context(store), Filter(), new Pipeline.Inline(chain => {
            chain.Context.Response.Status = 200;
            chain.Context.Response.ContentType = "application/json";

            return Task.CompletedTask;
        })).Next();

        var second = Context(store);

        await Pipeline.Chain(second, Filter(), Writing("never")).Next();

        Assert.Equal(200, second.Response.Status);
        Assert.Equal("application/json", second.Response.ContentType);
    }

    /// <summary>
    /// The headers the response carried are replayed, so <c>Cache-Control</c> from a
    /// <c>[CacheControl]</c> filter at the same position still reaches a hit - whichever of the two
    /// the sort happened to order first.
    /// </summary>
    [Fact]
    public async Task AHitReplaysTheHeadersTheResponseCarried() {
        var store = new CacheTestSupport.RecordingStore();

        await Pipeline.Chain(Context(store), Filter(), new Pipeline.Inline(chain => {
            chain.Context.Response.Headers[KnownHeaders.CacheControl] =
                new StringValues("public, max-age=60");

            return Task.CompletedTask;
        })).Next();

        var second = Context(store);

        await Pipeline.Chain(second, Filter()).Next();

        Assert.Equal("public, max-age=60", second.Response.Headers[KnownHeaders.CacheControl]);
    }

    /// <summary>
    /// A <c>Set-Cookie</c> is about the caller rather than the representation, so it is dropped as
    /// the response is captured. Replaying one hands a second caller the first one's session.
    /// </summary>
    [Fact]
    public async Task ASetCookieIsNeverReplayed() {
        var store = new CacheTestSupport.RecordingStore();

        await Pipeline.Chain(Context(store), Filter(), new Pipeline.Inline(chain => {
            chain.Context.Response.Headers[KnownHeaders.SetCookie] = new StringValues("session=abc");

            return Task.CompletedTask;
        })).Next();

        var second = Context(store);

        await Pipeline.Chain(second, Filter()).Next();

        Assert.False(second.Response.Headers.ContainsKey(KnownHeaders.SetCookie));
    }

    /// <summary>
    /// A strategy that returns null neither looks the request up nor stores it.
    /// </summary>
    [Fact]
    public async Task ANullKeyLeavesTheRequestUncached() {
        var store = new CacheTestSupport.RecordingStore();
        var context = Context(store);

        var chain = Pipeline.Chain(
            context,
            Filter(providers: new CacheTestSupport.Keyed(_ => null)),
            Writing("catalog"));

        await chain.Next();

        Assert.Equal("catalog", BodyOf(context));
        Assert.Empty(store.Reads);
        Assert.Empty(store.Writes);
    }

    /// <summary>
    /// Two strategies compose into one key rather than two lookups, and the parts appear in the
    /// order they were declared.
    /// </summary>
    [Fact]
    public async Task TwoStrategiesComposeOneKey() {
        var store = new CacheTestSupport.RecordingStore();

        var chain = Pipeline.Chain(
            Context(store),
            Filter(providers: [new CacheTestSupport.FixedKey(), new CacheTestSupport.SecondKey()]),
            Writing("catalog"));

        await chain.Next();

        Assert.Equal("GET /catalog" + Separator + "fixed" + Separator + "second", Assert.Single(store.Reads));
    }

    /// <summary>
    /// One part of a composite key declining leaves the whole request uncached. There is no partial
    /// key: a response varying on something a strategy could not read must not be shared.
    /// </summary>
    [Fact]
    public async Task OneStrategyDecliningLeavesACompositeRequestUncached() {
        var store = new CacheTestSupport.RecordingStore();

        var chain = Pipeline.Chain(
            Context(store),
            Filter(providers: [new CacheTestSupport.FixedKey(), new CacheTestSupport.Keyed(_ => null)]),
            Writing("catalog"));

        await chain.Next();

        Assert.Empty(store.Reads);
    }

    [Fact]
    public async Task TheDeclaredDurationIsWhatIsStored() {
        var store = new CacheTestSupport.RecordingStore();

        await Pipeline.Chain(Context(store), Filter(duration: 300), Writing("catalog")).Next();

        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(store.Writes).Duration);
    }

    [Fact]
    public async Task NoDeclaredDurationIsSixtySeconds() {
        var store = new CacheTestSupport.RecordingStore();

        await Pipeline.Chain(Context(store), Filter(), Writing("catalog")).Next();

        Assert.Equal(
            TimeSpan.FromSeconds(ResponseCacheFilter.DefaultDuration),
            Assert.Single(store.Writes).Duration);
    }

    /// <summary>
    /// A failure is about the moment rather than the resource. Storing one serves it for a minute
    /// after whatever caused it has gone away.
    /// </summary>
    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(302)]
    [InlineData(304)]
    public async Task OnlyA200IsStored(int status) {
        var store = new CacheTestSupport.RecordingStore();

        await Pipeline.Chain(Context(store), Filter(), new Pipeline.Inline(chain => {
            chain.Context.Response.Status = status;

            return Task.CompletedTask;
        })).Next();

        Assert.Empty(store.Writes);
    }

    /// <summary>
    /// A handler that threw is not stored, and the response the exception path wrote still reaches
    /// the client - the buffer is copied out in a <c>finally</c>, so a failure is not an empty body.
    /// </summary>
    [Fact]
    public async Task AFailedResponseIsWrittenAndNotStored() {
        var store = new CacheTestSupport.RecordingStore();
        var context = Context(store);

        await Pipeline.Chain(context, Filter(), new Pipeline.Inline(async chain => {
            await Write(chain.Context, "it broke");

            chain.Context.Response.ExceptionValue = new InvalidOperationException("it broke");
        })).Next();

        Assert.Equal("it broke", BodyOf(context));
        Assert.Empty(store.Writes);
    }

    /// <summary>
    /// An exception out of the chain leaves the response written rather than swallowed in a buffer
    /// nobody reads.
    /// </summary>
    [Fact]
    public async Task AThrowingChainStillWritesWhatItHadWritten() {
        var store = new CacheTestSupport.RecordingStore();
        var context = Context(store);

        var chain = Pipeline.Chain(context, Filter(), new Pipeline.Inline(async chain => {
            await Write(chain.Context, "partial");

            throw new InvalidOperationException("it broke");
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => chain.Next());

        Assert.Equal("partial", BodyOf(context));
    }

    /// <summary>
    /// Two handlers keyed the same way do not answer each other's requests, because the handler's
    /// method and path are in front of every key.
    /// </summary>
    [Fact]
    public async Task TwoHandlersKeyedAlikeDoNotShareEntries() {
        var store = new CacheTestSupport.RecordingStore();

        var catalog = new ResponseCacheFilter([new CacheTestSupport.FixedKey()], "GET /catalog", 60);
        var basket = new ResponseCacheFilter([new CacheTestSupport.FixedKey()], "GET /basket", 60);

        await Pipeline.Chain(Context(store), catalog, Writing("catalog")).Next();

        var second = Context(store);

        await Pipeline.Chain(second, basket, Writing("basket")).Next();

        Assert.Equal("basket", BodyOf(second));
    }

    /// <summary>
    /// The store is the whole opt-in, so a handler that declares caching in an application with no
    /// store says so rather than quietly serving uncached.
    /// </summary>
    [Fact]
    public async Task NoRegisteredStoreNamesTheHandler() {
        var chain = Pipeline.Chain(Pipeline.Context(), Filter(), Writing("catalog"));

        var exception =
            await Assert.ThrowsAsync<ResponseCacheStoreMissingException>(() => chain.Next());

        Assert.Equal("GET /catalog", exception.Handler);
        Assert.Contains("Hardened.Requests.Caching.Memory", exception.Message);
    }

    /// <summary>
    /// The authorization bypass this filter shipped with, found in the 0.19.0-rc1000 trial.
    /// </summary>
    /// <remarks>
    /// <c>AuthorizationFilter</c> and <c>RateLimitFilter</c> sit ahead of this stage and refuse by
    /// recording the failure and calling <c>Next</c>, so the serialization filter behind can write
    /// it. Replaying a stored 200 over that record answered the refused caller with an entry a
    /// permitted caller had filled.
    /// </remarks>
    [Fact]
    public async Task ARefusedRequestIsNotAnsweredFromTheStore() {
        var store = new CacheTestSupport.RecordingStore();

        await Pipeline.Chain(Context(store), Filter(), Writing("secret")).Next();

        var refused = Context(store);

        refused.Response.ExceptionValue = new UnauthorizedAccessException("no grant");

        // The warming request read the store on its way to missing. What matters is what the
        // refused one does, so only its reads are in scope.
        store.Reads.Clear();

        // Nothing stands in for the handler here, because nothing runs one: the serialization
        // filter reads the same record and writes the refusal instead of binding and invoking.
        await Pipeline.Chain(refused, Filter()).Next();

        Assert.Equal("", BodyOf(refused));
        Assert.Empty(store.Reads);
    }

    /// <summary>
    /// The refusal survives the stage, so the filter behind still has one to write.
    /// </summary>
    [Fact]
    public async Task ARefusedRequestKeepsItsRefusal() {
        var store = new CacheTestSupport.RecordingStore();
        var refusal = new UnauthorizedAccessException("no grant");
        var context = Context(store);

        context.Response.ExceptionValue = refusal;

        await Pipeline.Chain(context, Filter()).Next();

        Assert.Same(refusal, context.Response.ExceptionValue);
    }

    /// <summary>
    /// A refused request continues down the chain, which is what lets the serialization filter
    /// write the refusal. Returning here instead would answer nothing at all.
    /// </summary>
    [Fact]
    public async Task ARefusedRequestStillReachesTheFilterThatWritesIt() {
        var store = new CacheTestSupport.RecordingStore();
        var context = Context(store);
        var reached = false;

        context.Response.ExceptionValue = new UnauthorizedAccessException("no grant");

        await Pipeline.Chain(context, Filter(), new Pipeline.Inline(_ => {
            reached = true;

            return Task.CompletedTask;
        })).Next();

        Assert.True(reached);
    }

    /// <summary>
    /// Nor does a refused request fill the store. A key computed from a request nobody was allowed
    /// to make is one the next caller would hit.
    /// </summary>
    [Fact]
    public async Task ARefusedRequestIsNotStored() {
        var store = new CacheTestSupport.RecordingStore();
        var context = Context(store);

        context.Response.ExceptionValue = new UnauthorizedAccessException("no grant");

        await Pipeline.Chain(context, Filter(), Writing("secret")).Next();

        Assert.Empty(store.Writes);
    }

    private static Pipeline.Inline Writing(string body, Action? onRun = null) =>
        new(async chain => {
            onRun?.Invoke();

            await Write(chain.Context, body);
        });

    private static Task Write(IExecutionContext context, string body) {
        var bytes = Encoding.UTF8.GetBytes(body);

        return context.Response.Body.WriteAsync(bytes, 0, bytes.Length, context.CancellationToken);
    }
}
