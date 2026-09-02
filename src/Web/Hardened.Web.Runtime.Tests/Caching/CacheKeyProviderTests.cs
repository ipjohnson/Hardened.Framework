using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Caching;

/// <summary>
/// What the HTTP strategies put in a cache key, and what they refuse to be built from.
/// </summary>
public class CacheKeyProviderTests {

    private static IExecutionContext Context(
        IDictionary<string, string>? query = null,
        IDictionary<string, StringValues>? headers = null,
        IPathTokenCollection? pathTokens = null) {
        var provider = new ServiceCollection().BuildServiceProvider();

        var request = new TestExecutionRequest(
            "GET", "/catalog", "application/json", new SimpleQueryStringCollection(query)) {
            Headers = headers ?? new Dictionary<string, StringValues>()
        };

        if (pathTokens != null) {
            request.PathTokens = pathTokens;
        }

        return new TestExecutionContext(
            provider,
            provider,
            Substitute.For<IKnownServices>(),
            request,
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None);
    }

    private static async Task<string?> KeyOf(ICacheKeyProvider provider, IExecutionContext context) =>
        await provider.Key(context);

    #region VaryByQuery

    [Fact]
    public async Task VaryByQueryReadsTheKeysItWasNamed() {
        var context = Context(query: new Dictionary<string, string> {
            { "culture", "en-GB" },
            { "region", "eu" }
        });

        var key = await KeyOf(VaryByQuery.Create(["culture", "region"]), context);

        Assert.Equal("culture=en-GB&region=eu&", key);
    }

    /// <summary>
    /// Named keys rather than the whole query string. A cache keyed on everything is one a caller
    /// misses at will by adding a parameter nothing reads.
    /// </summary>
    [Fact]
    public async Task VaryByQueryIgnoresAKeyItWasNotNamed() {
        var withExtra = Context(query: new Dictionary<string, string> {
            { "culture", "en-GB" },
            { "utm_source", "somewhere" }
        });

        var without = Context(query: new Dictionary<string, string> { { "culture", "en-GB" } });

        Assert.Equal(
            await KeyOf(VaryByQuery.Create(["culture"]), without),
            await KeyOf(VaryByQuery.Create(["culture"]), withExtra));
    }

    /// <summary>
    /// The name is in the key as well as the value, so two keys whose values would concatenate the
    /// same way stay distinct.
    /// </summary>
    [Fact]
    public async Task VaryByQueryDistinguishesValuesThatWouldConcatenateAlike() {
        var first = Context(query: new Dictionary<string, string> { { "a", "xy" }, { "b", "" } });
        var second = Context(query: new Dictionary<string, string> { { "a", "x" }, { "b", "y" } });

        Assert.NotEqual(
            await KeyOf(VaryByQuery.Create(["a", "b"]), first),
            await KeyOf(VaryByQuery.Create(["a", "b"]), second));
    }

    [Fact]
    public async Task VaryByQueryTreatsAnAbsentKeyAsEmpty() {
        var context = Context(query: new Dictionary<string, string>());

        Assert.Equal("culture=&", await KeyOf(VaryByQuery.Create(["culture"]), context));
    }

    [Fact]
    public void VaryByQueryNeedsAtLeastOneKey() {
        Assert.Throws<ArgumentException>(() => VaryByQuery.Create([]));
    }

    #endregion

    #region VaryByHeader

    [Fact]
    public async Task VaryByHeaderReadsTheHeadersItWasNamed() {
        var context = Context(headers: new Dictionary<string, StringValues> {
            { "Accept-Language", new StringValues("en-GB") }
        });

        Assert.Equal(
            "Accept-Language=en-GB&",
            await KeyOf(VaryByHeader.Create(["Accept-Language"]), context));
    }

    /// <summary>
    /// Header names are case-insensitive over the wire. API Gateway's HTTP API delivers them
    /// lowercased, and a key that read one as absent would store one entry per casing.
    /// </summary>
    [Fact]
    public async Task VaryByHeaderReadsAHeaderWhateverItsCasing() {
        var lowercased = Context(headers: new Dictionary<string, StringValues> {
            { "accept-language", new StringValues("en-GB") }
        });

        Assert.Equal(
            "Accept-Language=en-GB&",
            await KeyOf(VaryByHeader.Create(["Accept-Language"]), lowercased));
    }

    /// <summary>
    /// A response varying on a header that does not say so is one a shared cache in front of this
    /// service serves to the wrong caller. ASP.NET Core's <c>VaryByHeaderNames</c> does not write
    /// this.
    /// </summary>
    [Fact]
    public async Task VaryByHeaderWritesVaryOnTheResponse() {
        var context = Context();

        await KeyOf(VaryByHeader.Create(["Accept-Language", "Accept-Encoding"]), context);

        Assert.Equal(
            "Accept-Language, Accept-Encoding",
            context.Response.Headers[KnownHeaders.Vary]);
    }

    [Fact]
    public void VaryByHeaderNeedsAtLeastOneName() {
        Assert.Throws<ArgumentException>(() => VaryByHeader.Create([]));
    }

    /// <summary>
    /// A response keyed on a session has one caller, so the entry is never hit and the name reads as
    /// though it might be.
    /// </summary>
    [Fact]
    public void VaryByHeaderRefusesCookie() {
        var exception = Assert.Throws<ArgumentException>(() => VaryByHeader.Create(["cookie"]));

        Assert.Contains("Cookie", exception.Message);
    }

    #endregion

    #region VaryByRoute

    [Fact]
    public async Task VaryByRouteReadsEveryToken() {
        var tokens = new PathTokenCollection(2, ["ownerId", "petId"]);

        tokens.SetValue(0, "7");
        tokens.SetValue(1, "3");

        Assert.Equal(
            "ownerId=7&petId=3&",
            await KeyOf(VaryByRoute.Create([]), Context(pathTokens: tokens)));
    }

    /// <summary>
    /// A route with no tokens keys on the route alone, which the filter already puts in front of
    /// every key. That is a cache of one entry, which is what a collection endpoint should have.
    /// </summary>
    [Fact]
    public async Task ARouteWithNoTokensKeysAsEmpty() {
        Assert.Equal(string.Empty, await KeyOf(VaryByRoute.Create([]), Context()));
    }

    [Fact]
    public void VaryByRouteTakesNoValues() {
        var exception = Assert.Throws<ArgumentException>(() => VaryByRoute.Create(["culture"]));

        Assert.Contains("VaryByQuery", exception.Message);
    }

    #endregion
}
