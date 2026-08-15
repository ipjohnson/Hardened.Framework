using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// Routes that compete for the same request.
///
/// <para>
/// <c>RouteTreeMatchingTests</c> covers the shapes a single route can take. These cover what
/// happens when two of them overlap: a literal against a token at the same depth, one token with
/// two different suffixes, two routes whose tree nodes reduce to the same generated method name,
/// and the same path under different verbs binding different token names. Every one of those
/// emits C# that compiles whichever way the generator resolved it, so the only assertion worth
/// making is which handler a request actually reaches.
/// </para>
/// </summary>
public class RouteTreeConflictTests {

    private const string EntryPoint = """
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        """;

    private static GeneratedRoutingTable Routing(string controllers) =>
        GeneratedRoutingTable.For(EntryPoint + controllers);

    /// <summary>
    /// <c>/a/x</c> and <c>/b/x</c> produce two tree nodes with no remaining path and two with the
    /// path <c>/x</c>, and the generated method name is derived from that path alone. Without the
    /// disambiguating counter in <c>GetRouteMethodName</c> the second pair overwrites the first
    /// and both requests reach one handler.
    /// </summary>
    [Fact]
    public void RoutesThatReduceToTheSameGeneratedMethodNameReachTheirOwnHandlers() {
        var routing = Routing("""
            public class SplitController {
                [Get("/a/x")]
                public string AX() => "ax";

                [Get("/b/x")]
                public string BX() => "bx";
            }
            """);

        Assert.Equal("AX", routing.Handler("GET", "/a/x").InvokeMethod);
        Assert.Equal("BX", routing.Handler("GET", "/b/x").InvokeMethod);
    }

    private const string LiteralAgainstToken = """
        public class MixController {
            [Get("/r/{id}")]
            public string Token(string id) => id;

            [Get("/r/fixed")]
            public string Fixed() => "fixed";

            [Get("/r/{id}/sub")]
            public string TokenSub(string id) => id;

            [Get("/r/fixed/sub")]
            public string FixedSub() => "fixed-sub";
        }
        """;

    /// <summary>
    /// A literal beats a token at the same depth, and it keeps beating it one segment deeper. The
    /// wildcard branch is only consulted once every literal branch has returned null, so the order
    /// holds however many levels of literal there are.
    /// </summary>
    [Theory]
    [InlineData("/r/fixed", "Fixed")]
    [InlineData("/r/other", "Token")]
    [InlineData("/r/fixed/sub", "FixedSub")]
    [InlineData("/r/other/sub", "TokenSub")]
    public void ALiteralBeatsATokenAtEveryDepth(string path, string expectedHandler) {
        Assert.Equal(expectedHandler, Routing(LiteralAgainstToken).Handler("GET", path).InvokeMethod);
    }

    /// <summary>
    /// The value bound for the token is the segment that was actually there, including when it is
    /// the prefix of a literal the tree also knows about.
    /// </summary>
    [Fact]
    public void ATokenBindsTheSegmentItMatchedEvenWhenALiteralSharesItsPrefix() {
        var routing = Routing(LiteralAgainstToken);

        Assert.Equal("fixe", Assert.Contains("id", routing.PathTokens("GET", "/r/fixe")));
        Assert.Equal("fixedly", Assert.Contains("id", routing.PathTokens("GET", "/r/fixedly")));
    }

    private const string SharedTokenDifferentSuffixes = """
        public class FileController {
            [Get("/f/{id}.json")]
            public string Json(string id) => id;

            [Get("/f/{id}/edit")]
            public string Edit(string id) => id;
        }
        """;

    /// <summary>
    /// One token position, two different things after it. The generator emits a wildcard match per
    /// distinct following character and tries them in turn, so a route is only reachable if the
    /// scan-forward loop stops at the right place for each.
    /// </summary>
    [Theory]
    [InlineData("/f/7.json", "Json")]
    [InlineData("/f/7/edit", "Edit")]
    public void OneTokenWithTwoDifferentSuffixesReachesBothRoutes(string path, string expectedHandler) {
        Assert.Equal(expectedHandler, Routing(SharedTokenDifferentSuffixes).Handler("GET", path).InvokeMethod);
    }

    /// <summary>
    /// The token stops before the suffix that told the two routes apart, rather than swallowing
    /// it. A token that took the whole remainder would still route correctly and hand the handler
    /// <c>7.json</c> as its id.
    /// </summary>
    [Fact]
    public void ATokenStopsBeforeTheSuffixThatDistinguishesTheRoutes() {
        var routing = Routing(SharedTokenDifferentSuffixes);

        Assert.Equal("7", Assert.Contains("id", routing.PathTokens("GET", "/f/7.json")));
        Assert.Equal("7", Assert.Contains("id", routing.PathTokens("GET", "/f/7/edit")));
    }

    /// <summary>
    /// A suffix neither route declares matches nothing, rather than falling into whichever
    /// wildcard branch was emitted first.
    /// </summary>
    [Fact]
    public void ASuffixNeitherRouteDeclaresMatchesNothing() {
        Assert.Null(Routing(SharedTokenDifferentSuffixes).Route("GET", "/f/7.xml"));
    }

    /// <summary>
    /// The token names belong to the route that won, not to the position. Two verbs on one path
    /// naming their token differently is the smallest case where a positional mistake is visible.
    /// </summary>
    [Fact]
    public void TheSamePathUnderTwoVerbsBindsEachVerbsOwnTokenName() {
        var routing = Routing("""
            public class TokenVerbController {
                [Get("/t/{getId}")]
                public string Read(string getId) => getId;

                [Post("/t/{postId}")]
                public string Write(string postId) => postId;
            }
            """);

        var read = routing.PathTokens("GET", "/t/7");
        var write = routing.PathTokens("POST", "/t/7");

        Assert.Equal("7", Assert.Contains("getId", read));
        Assert.DoesNotContain("postId", read);

        Assert.Equal("7", Assert.Contains("postId", write));
        Assert.DoesNotContain("getId", write);
    }

    /// <summary>
    /// A verb with no route on a path that only exists behind a token. The wildcard leaf has its
    /// own switch over the request method with its own default, separate from the literal one.
    /// </summary>
    [Fact]
    public void AVerbWithNoRouteBehindATokenReachesNoHandler() {
        var routing = Routing("""
            public class ReadOnlyController {
                [Get("/items/{id}")]
                public string GetItem(string id) => id;
            }
            """);

        Assert.NotNull(routing.Handler("GET", "/items/42"));
        Assert.Null(routing.Route("DELETE", "/items/42")?.Handler);
        Assert.Null(routing.Route("PATCH", "/items/42")?.Handler);
    }

    /// <summary>
    /// Three routes sharing one token position, each with a different name and a different depth
    /// below it. Each binds its own names and nothing else's.
    /// </summary>
    [Fact]
    public void ThreeRoutesSharingATokenPositionEachBindOnlyTheirOwnNames() {
        var routing = Routing("""
            public class NestedController {
                [Get("/n/{alpha}")]
                public string One(string alpha) => alpha;

                [Get("/n/{beta}/x")]
                public string Two(string beta) => beta;

                [Get("/n/{gamma}/x/{delta}")]
                public string Three(string gamma, string delta) => gamma + delta;
            }
            """);

        var one = routing.PathTokens("GET", "/n/1");
        var two = routing.PathTokens("GET", "/n/2/x");
        var three = routing.PathTokens("GET", "/n/3/x/4");

        Assert.Equal("1", Assert.Contains("alpha", one));
        Assert.DoesNotContain("beta", one);

        Assert.Equal("2", Assert.Contains("beta", two));
        Assert.DoesNotContain("alpha", two);

        Assert.Equal("3", Assert.Contains("gamma", three));
        Assert.Equal("4", Assert.Contains("delta", three));
        Assert.DoesNotContain("beta", three);
    }

    /// <summary>Two tokens with nothing but the leading slash in front of them.</summary>
    [Fact]
    public void TwoTokensAtTheRootOfTheTreeEachBindTheirOwnSegment() {
        var routing = Routing("""
            public class RootTokenController {
                [Get("/{tenant}/{resource}")]
                public string Pair(string tenant, string resource) => tenant + resource;
            }
            """);

        var tokens = routing.PathTokens("GET", "/acme/orders");

        Assert.Equal("acme", Assert.Contains("tenant", tokens));
        Assert.Equal("orders", Assert.Contains("resource", tokens));
    }

    /// <summary>
    /// A request that is a prefix of a route, or that continues past its end, matches nothing. The
    /// route test compares a span length against an index before it compares any character, so an
    /// off-by-one there matches every longer path.
    /// </summary>
    [Theory]
    [InlineData("/short", true)]
    [InlineData("/shorter", false)]
    [InlineData("/shor", false)]
    [InlineData("/", false)]
    [InlineData("", false)]
    public void ARequestMustBeExactlyAsLongAsTheRouteItMatches(string path, bool matches) {
        var routing = Routing("""
            public class ShortController {
                [Get("/short")]
                public string Value() => "short";
            }
            """);

        Assert.Equal(matches, routing.Route("GET", path) != null);
    }

    /// <summary>
    /// An empty segment is a segment. There is no normalisation between the transport and the
    /// route table, so <c>/a//b</c> and <c>/a/b</c> are two different paths and a route declared
    /// as one is unreachable as the other.
    /// </summary>
    [Fact]
    public void AnEmptySegmentInTheMiddleOfARouteIsMatchedLiterally() {
        var routing = Routing("""
            public class DoubleController {
                [Get("/a//b")]
                public string Value() => "double";
            }
            """);

        Assert.Equal("Value", routing.Handler("GET", "/a//b").InvokeMethod);
        Assert.Null(routing.Route("GET", "/a/b"));
    }

    /// <summary>
    /// A route with a token is matched as written, tokens included - the token scan and the
    /// literal comparison either side of it have to agree about that.
    /// </summary>
    [Theory]
    [InlineData("/ORDERS/7/LINES")]
    [InlineData("/Orders/7/Lines")]
    public void ARouteWithATokenIsMatchedAsWritten(string path) {
        var routing = Routing("""
            public class CaseController {
                [Get("/orders/{id}/lines")]
                public string Lines(string id) => id;
            }
            """);

        Assert.Equal("Lines", routing.Handler("GET", "/orders/7/lines").InvokeMethod);
        Assert.Null(routing.Route("GET", path));
    }

    /// <summary>
    /// And under <c>[CaseInsensitiveRoutes]</c> every spelling reaches it, with the token still
    /// bound - the scan that finds where a token ends runs between the two literal comparisons, so
    /// all three have to agree about case.
    /// </summary>
    [Theory]
    [InlineData("/orders/7/lines")]
    [InlineData("/ORDERS/7/LINES")]
    [InlineData("/Orders/7/Lines")]
    public void CaseInsensitiveRoutesMatchesATokenRouteInAnyCase(string path) {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            [CaseInsensitiveRoutes]
            public partial class TestApplication { }

            public class CaseController {
                [Get("/orders/{id}/lines")]
                public string Lines(string id) => id;
            }
            """);

        Assert.Equal("Lines", routing.Handler("GET", path).InvokeMethod);
        Assert.Equal("7", Assert.Contains("id", routing.PathTokens("GET", path)));
    }

    /// <summary>
    /// Routes diverging one character apart, where one is a strict prefix of the other and a third
    /// carries a token in the same position. The character switch, the literal comparison and the
    /// wildcard scan all decide this one request.
    /// </summary>
    [Fact]
    public void APrefixALongerLiteralAndATokenAtOnePositionAreAllReachable() {
        var routing = Routing("""
            public class PrefixController {
                [Get("/p/list")]
                public string List() => "list";

                [Get("/p/listing")]
                public string Listing() => "listing";

                [Get("/p/{id}")]
                public string ById(string id) => id;
            }
            """);

        Assert.Equal("List", routing.Handler("GET", "/p/list").InvokeMethod);
        Assert.Equal("Listing", routing.Handler("GET", "/p/listing").InvokeMethod);
        Assert.Equal("ById", routing.Handler("GET", "/p/lists").InvokeMethod);
        Assert.Equal("lists", Assert.Contains("id", routing.PathTokens("GET", "/p/lists")));
    }

    /// <summary>
    /// The same route declared on two controllers under two base paths, where the tails are
    /// identical. Both are reachable, and neither answers at the other's prefix.
    /// </summary>
    [Fact]
    public void IdenticalTailsUnderDifferentBasePathsDoNotCollide() {
        var routing = Routing("""
            [BasePath("/v1")]
            public class V1Controller {
                [Get("/items/{id}")]
                public string One(string id) => id;
            }

            [BasePath("/v2")]
            public class V2Controller {
                [Get("/items/{id}")]
                public string Two(string id) => id;
            }
            """);

        Assert.Equal("One", routing.Handler("GET", "/v1/items/7").InvokeMethod);
        Assert.Equal("Two", routing.Handler("GET", "/v2/items/7").InvokeMethod);
        Assert.Null(routing.Route("GET", "/items/7"));
    }
}
