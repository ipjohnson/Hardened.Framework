using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// The verb matrix, driven through the routing table the generator emits rather than asserted
/// against its source.
///
/// <para>
/// <c>DeleteAttribute</c> and <c>PatchAttribute</c> shipped from the first commit (2022-07-02) as
/// empty <c>internal</c> classes that did not derive from <see cref="Attribute"/>, so no project
/// could apply them, while the generator's verb list, the runtime, the README and the package
/// description all advertised both. Fixed 2026-08-11. Nothing here duplicates the end-to-end
/// coverage in <c>HttpMethodTests</c> — these assert the generator's half: that each verb reaches
/// the route table and dispatches on method as well as path.
/// </para>
/// </summary>
public class VerbRoutingTests {

    private const string FiveVerbController = """
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class ItemController {
            [Get("/items/{id}")]
            public string GetItem(string id) => id;

            [Post("/items/{id}")]
            public string PostItem(string id) => id;

            [Put("/items/{id}")]
            public string PutItem(string id) => id;

            [Delete("/items/{id}")]
            public string DeleteItem(string id) => id;

            [Patch("/items/{id}")]
            public string PatchItem(string id) => id;
        }
        """;

    [Theory]
    [InlineData("GET", "GetItem")]
    [InlineData("POST", "PostItem")]
    [InlineData("PUT", "PutItem")]
    [InlineData("DELETE", "DeleteItem")]
    [InlineData("PATCH", "PatchItem")]
    public void EveryVerbRoutesToItsOwnHandler(string method, string expectedHandler) {
        var routing = GeneratedRoutingTable.For(FiveVerbController);

        var handler = routing.Handler(method, "/items/42");

        Assert.Equal(expectedHandler, handler.InvokeMethod);
        Assert.Equal(method, handler.Method);
    }

    /// <summary>
    /// One path, five verbs, five handlers. Routing on path alone would make whichever route was
    /// emitted first answer all of them.
    /// </summary>
    [Fact]
    public void TheSamePathUnderDifferentVerbsReachesDifferentHandlers() {
        var routing = GeneratedRoutingTable.For(FiveVerbController);

        var handlers = new[] { "GET", "POST", "PUT", "DELETE", "PATCH" }
            .Select(method => routing.Handler(method, "/items/42").InvokeMethod)
            .ToArray();

        Assert.Equal(handlers.Length, handlers.Distinct().Count());
    }

    [Fact]
    public void AVerbWithNoRouteOnAKnownPathDoesNotMatch() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class ReadOnlyController {
                [Get("/items/{id}")]
                public string GetItem(string id) => id;
            }
            """);

        Assert.NotNull(routing.Route("GET", "/items/42"));
        Assert.Null(routing.Route("DELETE", "/items/42"));
    }

    [Fact]
    public void AnUnknownPathDoesNotMatch() {
        var routing = GeneratedRoutingTable.For(FiveVerbController);

        Assert.Null(routing.Route("GET", "/nothing/here"));
    }

    /// <summary>
    /// The method is matched exactly, against the upper-cased verb the attribute name produces.
    /// A transport handing over a lower-case method finds nothing, which is correct per RFC 9110
    /// (method names are case-sensitive) and worth pinning because the path half of the match is
    /// deliberately case-insensitive.
    /// </summary>
    [Fact]
    public void TheRequestMethodIsMatchedCaseSensitively() {
        var routing = GeneratedRoutingTable.For(FiveVerbController);

        Assert.NotNull(routing.Route("GET", "/items/42"));
        Assert.Null(routing.Route("get", "/items/42"));
    }

    /// <summary>
    /// The token value belongs to the request, and the name to the route. Both have to survive the
    /// match or a handler binds its parameters from the wrong slot.
    /// </summary>
    [Fact]
    public void APathTokenBindsItsValueOnEveryVerb() {
        var routing = GeneratedRoutingTable.For(FiveVerbController);

        foreach (var method in new[] { "GET", "POST", "PUT", "DELETE", "PATCH" }) {
            var tokens = routing.PathTokens(method, "/items/abc123");

            Assert.Equal("abc123", Assert.Contains("id", tokens));
        }
    }

    /// <summary>
    /// HEAD is GET without a body, so it reaches the GET handler rather than nothing.
    ///
    /// <para>
    /// Before the fall-through case existed a HEAD matched no route at all, which meant every
    /// endpoint in every Hardened application answered <c>curl -I</c> with a 404 — and health
    /// checkers, link validators, CDNs and proxies all probe that way. The body is discarded
    /// further out, in <c>HeadRequest</c>; routing's job is only to get there.
    /// </para>
    /// </summary>
    [Fact]
    public void HeadReachesTheGetHandler() {
        var routing = GeneratedRoutingTable.For(FiveVerbController);

        var handler = routing.Handler("HEAD", "/items/42");

        Assert.Equal("GetItem", handler.InvokeMethod);
    }

    /// <summary>
    /// The fall-through is attached to a GET leaf, not to the path. A resource that cannot be
    /// fetched cannot be probed either.
    /// </summary>
    [Fact]
    public void HeadDoesNotMatchAPathWithNoGetRoute() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class WriteOnlyController {
                [Post("/items/{id}")]
                public string PostItem(string id) => id;
            }
            """);

        Assert.Null(routing.Route("HEAD", "/items/42"));
    }

    /// <summary>
    /// A route with no token reaches its leaf through the leaf switch; one with a token reaches it
    /// through the switch inside the wildcard match method. Those are two separate emitters, and
    /// the fall-through has to be in both.
    /// </summary>
    [Fact]
    public void HeadReachesAGetHandlerOnATokenlessRoute() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";
            }
            """);

        Assert.Equal("List", routing.Handler("HEAD", "/orders").InvokeMethod);
    }

    /// <summary>
    /// The token still binds. HEAD runs the GET handler in full, so a handler that reads its path
    /// token has to receive it.
    /// </summary>
    [Fact]
    public void HeadBindsThePathTokensOfTheGetRoute() {
        var routing = GeneratedRoutingTable.For(FiveVerbController);

        var tokens = routing.PathTokens("HEAD", "/items/abc123");

        Assert.Equal("abc123", Assert.Contains("id", tokens));
    }

    /// <summary>
    /// Handlers on separate controllers land in one route table. Each controller generates its own
    /// invoke class, and the table has to reach all of them.
    /// </summary>
    [Fact]
    public void RoutesFromSeparateControllersShareOneRouteTable() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";
            }

            public class CustomerController {
                [Get("/customers")]
                public string List() => "customers";
            }
            """);

        Assert.Equal("OrderController", routing.Handler("GET", "/orders").HandlerType.Name);
        Assert.Equal("CustomerController", routing.Handler("GET", "/customers").HandlerType.Name);
    }
}
