using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// The routing table the generator writes into the application's partial class, compiled rather
/// than string-matched.
///
/// <para>
/// The route tree itself is covered by the tests under Web/Routing, which work on the tree
/// directly. These cover the step after it: turning that tree into C#, wiring the handler provider
/// into the service collection, and doing it in a file that builds.
/// </para>
/// </summary>
public class RoutingTableCompilesTests
{
    /// <summary>
    /// The routing table is only generated for a class carrying <c>[HardenedModule]</c>, and it is
    /// emitted as a partial of that class, so the application declaration has to be partial too.
    /// </summary>
    private static string Application(string controllers, string moduleAttributes = "") =>
        $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            {{moduleAttributes}}
            public partial class Application { }

            {{controllers}}
            """;

    [Fact]
    public void ARoutingTableIsGeneratedForTheApplicationModule()
    {
        var result = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders/{id}")]
                        public string GetOrder(string id) => id;
                    }
                    """
                )
            )
            .AssertNoErrors();

        var routing = result.SourceContaining("Routing");

        Assert.Contains(
            "private class RoutingTable : IWebExecutionRequestHandlerProvider",
            routing
        );
        Assert.Contains(
            "public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context, "
                + "ref PathTokenCollection pathTokens)",
            routing
        );
    }

    /// <summary>
    /// The routing table registers itself as the handler provider and each controller as transient.
    /// Miss the controller registration and every route resolves and then fails to construct.
    /// </summary>
    [Fact]
    public void TheRoutingTableRegistersItselfAndEveryController()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders")]
                        public string All() => "x";
                    }

                    public class CustomerController {
                        [Get("/customers")]
                        public string All() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("serviceCollection.AddTransient<OrderController>();", routing);
        Assert.Contains("serviceCollection.AddTransient<CustomerController>();", routing);
        Assert.Contains("AddSingleton<", routing);
    }

    /// <summary>
    /// One controller, two handlers. The controller is registered once — a duplicate registration
    /// is legal C# but silently doubles the service descriptors.
    /// </summary>
    [Fact]
    public void AControllerWithSeveralHandlersIsRegisteredOnce()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders")]
                        public string All() => "x";

                        [Get("/orders/{id}")]
                        public string One(string id) => id;
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        var occurrences = routing.Split("AddTransient<OrderController>()").Length - 1;

        Assert.Equal(1, occurrences);
    }

    /// <summary>
    /// An application with no handlers at all. The routing table is still generated, still
    /// registered, and still has to compile — it is what a freshly scaffolded project builds.
    /// </summary>
    [Fact]
    public void AnApplicationWithNoRoutesStillCompiles()
    {
        var result = RequestGeneratorHarness.Generate(Application("")).AssertNoErrors();

        Assert.Contains("RoutingTable", result.SourceContaining("Routing"));
    }

    [Theory]
    [InlineData("Get", "GET")]
    [InlineData("Post", "POST")]
    [InlineData("Put", "PUT")]
    [InlineData("Delete", "DELETE")]
    [InlineData("Patch", "PATCH")]
    public void EveryVerbReachesTheRoutingTableUppercased(string verb, string expected)
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    $$"""
                    public class ItemController {
                        [{{verb}}("/items")]
                        public string Handle() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains($"case \"{expected}\":", routing);
    }

    /// <summary>
    /// The same path under two verbs. One leaf node carries both, so the method switch is what
    /// keeps them apart.
    /// </summary>
    [Fact]
    public void ThePathIsMatchedOnceAndTheVerbSelectsTheHandler()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class ItemController {
                        [Get("/items")]
                        public string Read() => "x";

                        [Post("/items")]
                        public string Write() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("case \"GET\":", routing);
        Assert.Contains("case \"POST\":", routing);
    }

    /// <summary>
    /// Two routes sharing a prefix with a path token in the same position under different names.
    ///
    /// <para>
    /// The tree shares one node for that token position, so the node cannot know which name
    /// applies. Until 2026-08-11 the name was taken from whichever route registered first, and
    /// <c>/path/a/b</c> routed correctly to the second handler and then failed binding with "first
    /// was missing" — a 400 that appeared only when the route was called. Token names now belong to
    /// the route: each matched leaf carries its own names array.
    /// </para>
    ///
    /// <para>Asserted here at the emit level; OverlappingRouteTokenNamesTests covers the request.</para>
    /// </summary>
    [Fact]
    public void OverlappingRoutesEachGetTheirOwnTokenNames()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class BindingController {
                        [Get("/path/{id}")]
                        public string One(string id) => id;

                        [Get("/path/{first}/{second}")]
                        public string Two(string first, string second) => first + second;
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("new string[] { \"id\" }", routing);
        Assert.Contains("new string[] { \"first\", \"second\" }", routing);
    }

    /// <summary>
    /// The token names array is per route, not per token position, so two routes whose tokens sit
    /// at the same depth under different names each keep their own.
    /// </summary>
    [Fact]
    public void TwoRoutesWithADifferentlyNamedTokenAtTheSameDepthKeepBothNames()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class UserController {
                        [Get("/users/{id}")]
                        public string One(string id) => id;

                        [Get("/users/{userId}/posts/{postId}")]
                        public string Post(string userId, string postId) => userId + postId;
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("new string[] { \"id\" }", routing);
        Assert.Contains("new string[] { \"userId\", \"postId\" }", routing);
    }

    /// <summary>
    /// A route with no tokens never touches the token destination, and answers one record built on
    /// first use.
    /// </summary>
    [Fact]
    public void ARouteWithNoTokensBindsNothingAndReusesItsHandlerInfo()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class HealthController {
                        [Get("/health")]
                        public string Health() => "ok";
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.DoesNotContain("new PathTokenCollection(", routing);
        Assert.Contains("_infoHealthController_Health ??= new RequestHandlerInfo(", routing);
    }

    /// <summary>
    /// A route with a token answers the same one record, and writes what it captured into the
    /// destination it was handed.
    /// </summary>
    /// <remarks>
    /// The record used to carry the token values, which is what stopped a tokened route reusing
    /// one: the handler was cached and the record around it was not. Both halves matter, so both
    /// are asserted - a leaf that assigns the destination but rebuilds the record allocates the 40
    /// bytes this removed.
    /// </remarks>
    [Fact]
    public void ARouteWithATokenWritesItIntoTheDestinationAndReusesItsHandlerInfo()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders/{id}")]
                        public string Get(string id) => id;
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("pathTokens = new PathTokenCollection(", routing);

        // The handler type of a tokened route carries a hash of the template, so the field is
        // matched rather than spelled.
        Assert.Matches(@"_infoOrderController_Get\w* \?\?= new RequestHandlerInfo\(", routing);
    }

    /// <summary>
    /// <c>[BasePath]</c> on the controller prefixes every route it declares, and the prefix reaches
    /// the handler info as well as the tree.
    /// </summary>
    [Fact]
    public void ABasePathOnTheControllerPrefixesItsRoutes()
    {
        var result = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    [BasePath("/api/orders")]
                    public class OrderController {
                        [Get("/{id}")]
                        public string One(string id) => id;
                    }
                    """
                )
            )
            .AssertNoErrors();

        Assert.Contains("\"/api/orders/{id}\"", result.SourceContaining("One"));
    }

    /// <summary>
    /// <c>[BasePath]</c> on the application module prefixes every route in the assembly, which is
    /// how a whole service is mounted under one path.
    /// </summary>
    [Fact]
    public void ABasePathOnTheModulePrefixesEveryRoute()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders")]
                        public string All() => "x";
                    }
                    """,
                    "[BasePath(\"/v1\")]"
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("/v1/orders", routing);
    }

    /// <summary>
    /// A literal is compared whole, and against one case.
    /// </summary>
    /// <remarks>
    /// Two assertions because they are one emit. The comparison is a span comparison rather than
    /// one <c>==</c> per character: the JIT folds neither the comparisons nor the bounds checks, so
    /// a seven-character literal cost seven loads, seven branches and seven range checks the length
    /// guard did not remove. And it is ordinal, because paths are case-sensitive per RFC 3986 - the
    /// case-insensitive form is opt-in and asserted below.
    /// </remarks>
    [Fact]
    public void PathMatchingComparesALiteralWholeAndOrdinally()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders")]
                        public string All() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains(".SequenceEqual(\"/orders\")", routing);
        Assert.DoesNotContain("OrdinalIgnoreCase", routing);
    }

    /// <summary>
    /// Unless the module asks otherwise, in which case the same comparison runs case-insensitively.
    /// </summary>
    /// <remarks>
    /// One call rather than a second <c>==</c> per letter, which is what this used to emit.
    /// <see cref="MemoryExtensions.Equals(ReadOnlySpan{char}, ReadOnlySpan{char}, StringComparison)"/>
    /// is vectorised for ASCII, so the case-insensitive table stops costing double the comparisons
    /// of the case-sensitive one.
    /// </remarks>
    [Fact]
    public void CaseInsensitiveRoutesCompareIgnoringCase()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders")]
                        public string All() => "x";
                    }
                    """,
                    "[CaseInsensitiveRoutes]"
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("OrdinalIgnoreCase", routing);
        Assert.Contains("\"/orders\"", routing);
    }

    /// <summary>
    /// Handlers spread over several files. The routing table collects every handler in the
    /// compilation, so a controller in another file has to reach the same table.
    /// </summary>
    [Fact]
    public void HandlersFromSeveralFilesReachOneRoutingTable()
    {
        var result = RequestGeneratorHarness
            .Generate(
                new Dictionary<string, string>
                {
                    ["Application.cs"] = """
                    using Hardened.Shared.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class Application { }
                    """,
                    ["OrderController.cs"] = """
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    public class OrderController {
                        [Get("/orders")]
                        public string All() => "x";
                    }
                    """,
                    ["CustomerController.cs"] = """
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    public class CustomerController {
                        [Get("/customers")]
                        public string All() => "x";
                    }
                    """,
                }
            )
            .AssertNoErrors();

        var routing = result.SourceContaining("Routing");

        Assert.Contains("AddTransient<OrderController>();", routing);
        Assert.Contains("AddTransient<CustomerController>();", routing);
    }

    /// <summary>
    /// Two application modules in one compilation each get their own routing table, and both list
    /// every handler. Two files emitted under one hint name would silently drop one, which
    /// AssertNoErrors also checks.
    /// </summary>
    [Fact]
    public void TwoApplicationModulesEachGetTheirOwnRoutingTable()
    {
        var result = RequestGeneratorHarness
            .Generate(
                """
                using Hardened.Shared.Runtime.Attributes;
                using Hardened.Web.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                public partial class FirstApplication { }

                [HardenedModule]
                public partial class SecondApplication { }

                public class OrderController {
                    [Get("/orders")]
                    public string All() => "x";
                }
                """
            )
            .AssertNoErrors();

        Assert.Contains("FirstApplication.Routing.cs", result.GeneratedSources.Keys);
        Assert.Contains("SecondApplication.Routing.cs", result.GeneratedSources.Keys);
    }
}
