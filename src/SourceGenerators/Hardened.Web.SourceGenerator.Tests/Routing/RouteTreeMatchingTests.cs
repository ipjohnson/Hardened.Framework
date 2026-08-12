using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// Where routes collide.
///
/// <para>
/// The route tree is emitted as nested span comparisons — one method per shared path prefix, a
/// switch on the next character where prefixes diverge, and a scan-forward loop for tokens. All of
/// it compiles whether or not it matches correctly, so the only assertion worth making is that a
/// request reaches the handler it should. The tree-shape tests in
/// <c>Hardened.SourceGenerator.Tests/Web/Routing</c> cover the structure; these cover the match.
/// </para>
/// </summary>
public class RouteTreeMatchingTests {

    private const string OverlappingRoutes = """
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class UserController {
            [Get("/users/{id}")]
            public string GetUser(string id) => id;

            [Get("/users/{userId}/posts/{postId}")]
            public string GetPost(string userId, string postId) => userId + postId;

            [Get("/users/active")]
            public string Active() => "active";
        }
        """;

    /// <summary>
    /// Two routes sharing a token position, each binding its own name. The tree shares the node —
    /// it cannot know at that depth which route will match — so the names come from the leaf that
    /// wins and the values are filled in positionally as the match unwinds. Getting this wrong
    /// gives one route the other's token names, silently.
    /// </summary>
    [Fact]
    public void OverlappingRoutesBindTheirOwnTokenNames() {
        var routing = GeneratedRoutingTable.For(OverlappingRoutes);

        var single = routing.PathTokens("GET", "/users/42");

        Assert.Equal("42", Assert.Contains("id", single));
        Assert.DoesNotContain("userId", single);

        var nested = routing.PathTokens("GET", "/users/42/posts/9");

        Assert.Equal("42", Assert.Contains("userId", nested));
        Assert.Equal("9", Assert.Contains("postId", nested));
        Assert.DoesNotContain("id", nested);
    }

    /// <summary>
    /// A literal segment wins over a token in the same position. Wildcards are only consulted once
    /// the literal branches have failed, so <c>/users/active</c> is not swallowed by
    /// <c>/users/{id}</c>.
    /// </summary>
    [Fact]
    public void ALiteralSegmentWinsOverATokenInTheSamePosition() {
        var routing = GeneratedRoutingTable.For(OverlappingRoutes);

        Assert.Equal("Active", routing.Handler("GET", "/users/active").InvokeMethod);
        Assert.Equal("GetUser", routing.Handler("GET", "/users/inactive").InvokeMethod);
    }

    /// <summary>
    /// A token at the end of a route takes the whole remainder of the path, slashes included. That
    /// makes every trailing token a catch-all, which is what <c>{*name}</c> relies on — the
    /// asterisk is not syntax, it is just part of the token name.
    /// </summary>
    [Fact]
    public void ATrailingTokenTakesTheRestOfThePath() {
        var routing = GeneratedRoutingTable.For(OverlappingRoutes);

        var tokens = routing.PathTokens("GET", "/users/42/unmatched/tail");

        Assert.Equal("GetUser", routing.Handler("GET", "/users/42/unmatched/tail").InvokeMethod);
        Assert.Equal("42/unmatched/tail", Assert.Contains("id", tokens));
    }

    /// <summary>A token can match nothing at all; the segment before it still has to be there.</summary>
    [Fact]
    public void ATokenMatchesAnEmptyValueButTheSegmentBeforeItIsRequired() {
        var routing = GeneratedRoutingTable.For(OverlappingRoutes);

        Assert.Equal("", Assert.Contains("id", routing.PathTokens("GET", "/users/")));
        Assert.Null(routing.Route("GET", "/users"));
    }

    /// <summary>
    /// A trailing slash is part of the route. <c>/orders</c> and <c>/orders/</c> are two paths, and
    /// a route declared as one is not reachable as the other — there is no normalisation step
    /// anywhere between the transport and the route table.
    /// </summary>
    [Fact]
    public void ATrailingSlashIsSignificant() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class SlashController {
                [Get("/orders")]
                public string NoSlash() => "no-slash";

                [Get("/invoices/")]
                public string WithSlash() => "with-slash";
            }
            """);

        Assert.Equal("NoSlash", routing.Handler("GET", "/orders").InvokeMethod);
        Assert.Null(routing.Route("GET", "/orders/"));

        Assert.Equal("WithSlash", routing.Handler("GET", "/invoices/").InvokeMethod);
        Assert.Null(routing.Route("GET", "/invoices"));
    }

    /// <summary>
    /// Both spellings of a route declared in lower case reach it. The emitted comparison accepts
    /// either case for each character, and the character-switch nodes emit a case for each.
    /// </summary>
    [Theory]
    [InlineData("/orders/summary")]
    [InlineData("/ORDERS/SUMMARY")]
    [InlineData("/Orders/Summary")]
    [InlineData("/oRdErS/sUmMaRy")]
    public void ALowerCaseRouteMatchesARequestInAnyCase(string path) {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class OrderController {
                [Get("/orders/summary")]
                public string Summary() => "summary";
            }
            """);

        Assert.Equal("Summary", routing.Handler("GET", path).InvokeMethod);
    }

    /// <summary>
    /// Routes diverging after a shared prefix each keep their own tail. The generator emits one
    /// method for the shared characters and a switch on the character that differs, so an
    /// off-by-one in the index arithmetic sends every one of them to the same place.
    /// </summary>
    [Fact]
    public void RoutesSharingAPrefixEachKeepTheirOwnTail() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class PrefixController {
                [Get("/order")]
                public string Order() => "order";

                [Get("/orders")]
                public string Orders() => "orders";

                [Get("/orderable")]
                public string Orderable() => "orderable";

                [Get("/organisation")]
                public string Organisation() => "organisation";
            }
            """);

        Assert.Equal("Order", routing.Handler("GET", "/order").InvokeMethod);
        Assert.Equal("Orders", routing.Handler("GET", "/orders").InvokeMethod);
        Assert.Equal("Orderable", routing.Handler("GET", "/orderable").InvokeMethod);
        Assert.Equal("Organisation", routing.Handler("GET", "/organisation").InvokeMethod);
        Assert.Null(routing.Route("GET", "/orde"));
    }

    /// <summary>
    /// A token in the middle of a route stops at the next literal rather than consuming to the end,
    /// even when the literal that follows also appears inside the value.
    /// </summary>
    [Fact]
    public void AMiddleTokenStopsAtTheSegmentThatFollowsIt() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class FileController {
                [Get("/files/{name}/download")]
                public string Download(string name) => name;
            }
            """);

        Assert.Equal("a/b/c", Assert.Contains("name",
            routing.PathTokens("GET", "/files/a/b/c/download")));

        Assert.Null(routing.Route("GET", "/files/a/b/c"));
    }

    /// <summary>
    /// Two tokens back to back in one route. Each has its own position in the token collection, and
    /// the value written for one must not overwrite the other.
    /// </summary>
    [Fact]
    public void AdjacentTokensEachKeepTheirOwnValue() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class MatrixController {
                [Get("/matrix/{row}/{column}")]
                public string Cell(string row, string column) => row + column;
            }
            """);

        var tokens = routing.PathTokens("GET", "/matrix/7/9");

        Assert.Equal("7", Assert.Contains("row", tokens));
        Assert.Equal("9", Assert.Contains("column", tokens));
    }
}
