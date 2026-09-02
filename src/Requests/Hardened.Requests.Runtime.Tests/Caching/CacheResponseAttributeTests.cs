using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Caching;

/// <summary>
/// What the attribute installs, what it declines to install, and how several of them on one handler
/// become one filter.
/// </summary>
public class CacheResponseAttributeTests {

    [Fact]
    public void TheFilterIsInstalledAheadOfSerialization() {
        var attribute = new CacheResponseAttribute<CacheTestSupport.FixedKey>();
        var handler = CacheTestSupport.Handler([attribute]);

        var filter = Assert.Single(attribute.GetFilters(handler));

        Assert.Equal(FilterOrder.BeforeSerialization, filter.Order);
        Assert.IsType<ResponseCacheFilter>(filter.FilterFunc(null!));
    }

    /// <summary>
    /// One filter, not three. Each attribute is asked in turn and only the first builds anything;
    /// three filters would each look the request up and each store the answer.
    /// </summary>
    [Fact]
    public void ThreeDeclarationsOnOneHandlerProduceOneFilter() {
        var first = new CacheResponseAttribute<CacheTestSupport.FixedKey>();
        var second = new CacheResponseAttribute<CacheTestSupport.SecondKey>();
        var third = new CacheResponseAttribute<CacheTestSupport.FixedKey>();

        var handler = CacheTestSupport.Handler([first, second, third]);

        Assert.Single(first.GetFilters(handler));
        Assert.Empty(second.GetFilters(handler));
        Assert.Empty(third.GetFilters(handler));
    }

    /// <summary>
    /// A globally registered instance is not in the handler's metadata, so it applies to a handler
    /// that declares nothing.
    /// </summary>
    [Fact]
    public void AGlobalDeclarationAppliesToAHandlerThatDeclaresNone() {
        var global = new CacheResponseAttribute<CacheTestSupport.FixedKey>();

        Assert.Single(global.GetFilters(CacheTestSupport.Handler([])));
    }

    /// <summary>
    /// Explicit beats convention. The handler's own declarations compose the filter and the global
    /// one stands down, without the registration site having to say so.
    /// </summary>
    [Fact]
    public void AGlobalDeclarationStandsDownWhereAHandlerDeclaresItsOwn() {
        var global = new CacheResponseAttribute<CacheTestSupport.FixedKey>();
        var declared = new CacheResponseAttribute<CacheTestSupport.SecondKey>();

        Assert.Empty(global.GetFilters(CacheTestSupport.Handler([declared])));
    }

    /// <summary>
    /// The filter runs before authorization that reads bound parameters, so a handler guarded by one
    /// is not cached at all. ASP.NET Core ships this as a documentation note and the failure is
    /// silent.
    /// </summary>
    [Fact]
    public void AResourceScopedHandlerIsNotCached() {
        var attribute = new CacheResponseAttribute<CacheTestSupport.FixedKey>();

        var handler = CacheTestSupport.Handler(
            [attribute], requirement: Requirement.Predicate((_, _) => true));

        Assert.Empty(attribute.GetFilters(handler));
    }

    /// <summary>
    /// A requirement over grants alone settles before serialization, which is ahead of the cache, so
    /// it is safe to cache behind one.
    /// </summary>
    [Fact]
    public void AGrantOnlyHandlerIsStillCached() {
        var attribute = new CacheResponseAttribute<CacheTestSupport.FixedKey>();

        var handler = CacheTestSupport.Handler(
            [attribute], requirement: Requirement.Grant("catalog:read"));

        Assert.Single(attribute.GetFilters(handler));
    }

    /// <summary>
    /// <c>params string[]</c> cannot express "this strategy takes no values", so an argument a
    /// strategy cannot use compiles clean. The failure names the handler instead of being ignored.
    /// </summary>
    [Fact]
    public void AStrategyHandedValuesItCannotUseNamesTheHandler() {
        var attribute = new CacheResponseAttribute<CacheTestSupport.Unbuildable>("culture");
        var handler = CacheTestSupport.Handler([attribute], path: "/catalog", method: "GET");

        var exception = Assert.Throws<InvalidOperationException>(
            () => attribute.GetFilters(handler).ToList());

        Assert.Contains("GET /catalog", exception.Message);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task TheFirstDeclaredDurationWins() {
        var first = new CacheResponseAttribute<CacheTestSupport.FixedKey> { Duration = 300 };
        var second = new CacheResponseAttribute<CacheTestSupport.SecondKey>();

        Assert.Equal(TimeSpan.FromSeconds(300), await StoredDuration(first, second));
    }

    /// <summary>
    /// Composed attributes share one lifetime, so two that disagree is a mistake rather than a
    /// precedence question. It fails as the chain is built, naming the handler.
    /// </summary>
    [Fact]
    public void TwoDurationsThatDisagreeNameTheHandler() {
        var first = new CacheResponseAttribute<CacheTestSupport.FixedKey> { Duration = 300 };
        var second = new CacheResponseAttribute<CacheTestSupport.SecondKey> { Duration = 60 };

        var handler = CacheTestSupport.Handler([first, second]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => first.GetFilters(handler).ToList());

        Assert.Contains("GET /catalog", exception.Message);
        Assert.Contains("300", exception.Message);
        Assert.Contains("60", exception.Message);
    }

    /// <summary>
    /// Two of the same duration are not a disagreement.
    /// </summary>
    [Fact]
    public async Task TwoDurationsThatAgreeAreAccepted() {
        var first = new CacheResponseAttribute<CacheTestSupport.FixedKey> { Duration = 300 };
        var second = new CacheResponseAttribute<CacheTestSupport.SecondKey> { Duration = 300 };

        Assert.Equal(TimeSpan.FromSeconds(300), await StoredDuration(first, second));
    }

    /// <summary>
    /// The attribute's own values reach the strategy that was named.
    /// </summary>
    [Fact]
    public void ThePositionalArgumentsReachTheStrategy() {
        var attribute = new CacheResponseAttribute<RecordingProvider>("culture", "region");

        var provider = Assert.IsType<RecordingProvider>(attribute.CreateKeyProvider());

        Assert.Equal(["culture", "region"], provider.Values);
    }

    /// <summary>
    /// How long the filter these declarations compose asks the store to keep an entry, observed by
    /// serving one request through it rather than read off a field.
    /// </summary>
    private static async Task<TimeSpan> StoredDuration(params ICacheResponseDeclaration[] declared) {
        var handler = CacheTestSupport.Handler([..declared]);
        var filter = ResponseCacheFilter.Compose(handler, declared);
        var store = new CacheTestSupport.RecordingStore();

        var context = Pipeline.Context(
            configureServices: services => services.AddSingleton<IResponseCacheStore>(store));

        await Pipeline.Chain(context, filter).Next();

        return Assert.Single(store.Writes).Duration;
    }

    private sealed class RecordingProvider : ICacheKeyProvider {
        private RecordingProvider(string[] values) {
            Values = values;
        }

        public string[] Values { get; }

        public static ICacheKeyProvider Create(string[] values) => new RecordingProvider(values);

        public ValueTask<string?> Key(IExecutionContext context) => new("recorded");
    }
}
