using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// Breadth of <see cref="GeneratorResult.AssertNoErrors"/> across the shapes a real web
/// application declares.
///
/// <para>
/// <c>GeneratedCodeCompilesTests</c> compiles handler invoke classes, but none of its sources
/// declare a <c>[HardenedModule]</c> entry point — so <c>RoutingTableGenerator</c>, which is where
/// the route tree, the switch nodes, the wildcard matchers and the DI registration are emitted,
/// never runs in any of them. Every source here declares one, so each case compiles the invoke
/// classes <em>and</em> the routing table they are reached through.
/// </para>
///
/// <para>
/// These assert compilation only. Where a shape also has a routable outcome worth pinning, it is
/// driven through the compiled table in <c>RouteTreeConflictTests</c> instead.
/// </para>
/// </summary>
public class RouteCompilationTests {

    /// <summary>
    /// The header every case is generated against. Every using a case might need lives here rather
    /// than in the case itself: a file-scoped namespace ends the using section, so a using written
    /// alongside the controller is a CS1529 in the test's own source rather than anything the
    /// generator did.
    /// </summary>
    private const string ModuleEntryPoint = """
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Requests.Runtime.Filters;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        """;

    /// <summary>
    /// Generates and compiles a web application whose entry point is a <c>[HardenedModule]</c>
    /// partial class, so the routing table is emitted alongside the handlers.
    /// </summary>
    /// <remarks>
    /// The routing-table assertion is not redundant with <c>AssertNoErrors</c>.
    /// <c>SourceGeneratorWrapper</c> catches every exception thrown while emitting a file and
    /// reports it at <em>Warning</em> severity, so a generator that crashed mid-emit produces no
    /// error, no output, and a green <c>AssertNoErrors</c>. Requiring the file it should have
    /// written is what closes that.
    /// </remarks>
    private static GeneratorResult CompileApplication(string controllers) {
        var result = GeneratorTestHarness.Run(
            ModuleEntryPoint + controllers,
            new WebLibrarySourceGenerator(),
            GeneratedRoutingTable.Anchors);

        result.AssertNoErrors();

        var crashes = result.GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == "HardenedException")
            .ToArray();

        Assert.True(crashes.Length == 0,
            "The generator caught an exception and downgraded it to a warning, so it emitted " +
            "nothing and AssertNoErrors passed anyway:" + Environment.NewLine +
            string.Join(Environment.NewLine, crashes.Select(diagnostic => "  " + diagnostic.GetMessage())));

        Assert.Contains("TestApplication.Routing.cs", result.GeneratedSources.Keys);

        return result;
    }

    [Theory]
    [InlineData("Get")]
    [InlineData("Post")]
    [InlineData("Put")]
    [InlineData("Delete")]
    [InlineData("Patch")]
    public void EveryVerbCompilesIntoARoutingTable(string verb) {
        CompileApplication($$"""
            public class ItemController {
                [{{verb}}("/items/{id}")]
                public string Handle(string id) => id;
            }
            """);
    }

    /// <summary>
    /// One path under all five verbs. Every leaf of the route tree becomes a case in a single
    /// switch over the request method, and each carries its own handler field.
    /// </summary>
    [Fact]
    public void AllFiveVerbsOnOnePathCompileIntoOneRouteTree() {
        CompileApplication("""
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
            """);
    }

    /// <summary>
    /// Every verb again, this time under both base paths at once — the routing table's prefix and
    /// the controller's are folded in by two generators that do not know about each other.
    /// </summary>
    [Theory]
    [InlineData("Get")]
    [InlineData("Post")]
    [InlineData("Put")]
    [InlineData("Delete")]
    [InlineData("Patch")]
    public void EveryVerbCompilesUnderBothBasePaths(string verb) {
        GeneratorTestHarness.Run($$"""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            [BasePath("/module")]
            public partial class TestApplication { }

            [BasePath("/api")]
            public class ItemController {
                [{{verb}}("/items/{id}")]
                public string Handle(string id) => id;
            }
            """, new WebLibrarySourceGenerator(), GeneratedRoutingTable.Anchors).AssertNoErrors();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("[BasePath(\"/module\")]", "")]
    [InlineData("", "[BasePath(\"/api\")]")]
    [InlineData("[BasePath(\"/module\")]", "[BasePath(\"/api\")]")]
    public void EveryBasePathCombinationCompiles(string moduleAttribute, string controllerAttribute) {
        GeneratorTestHarness.Run($$"""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            {{moduleAttribute}}
            public partial class TestApplication { }

            {{controllerAttribute}}
            public class OrderController {
                [Get("/orders/{id}")]
                public string GetOrder(string id) => id;

                [Delete("/orders/{id}")]
                public string DeleteOrder(string id) => id;
            }
            """, new WebLibrarySourceGenerator(), GeneratedRoutingTable.Anchors).AssertNoErrors();
    }

    /// <summary>
    /// A base path that is the empty string. The generator concatenates it unconditionally, so the
    /// degenerate value has to leave the route reachable at its own path rather than at <c>""</c>.
    /// </summary>
    [Fact]
    public void AnEmptyModuleBasePathCompiles() {
        GeneratorTestHarness.Run("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            [BasePath("")]
            public partial class TestApplication { }

            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";
            }
            """, new WebLibrarySourceGenerator(), GeneratedRoutingTable.Anchors).AssertNoErrors();
    }

    /// <summary>
    /// Two entry points in one assembly. Each gets its own routing table over the same handlers,
    /// and the two tables must not collide on a hint name or on a generated field.
    /// </summary>
    [Fact]
    public void TwoModuleEntryPointsEachCompileTheirOwnRoutingTable() {
        var result = GeneratorTestHarness.Run("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class FirstApplication { }

            [HardenedModule]
            public partial class SecondApplication { }

            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";
            }
            """, new WebLibrarySourceGenerator(), GeneratedRoutingTable.Anchors).AssertNoErrors();

        Assert.Contains("FirstApplication.Routing.cs", result.GeneratedSources.Keys);
        Assert.Contains("SecondApplication.Routing.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The route shapes the tree generator branches on: literals, a token in every position, a
    /// bare root, adjacent tokens, and the characters <c>GetRouteMethodName</c> has to rewrite
    /// before they can appear in a C# identifier.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/a")]
    [InlineData("/a/")]
    [InlineData("/{id}")]
    [InlineData("/a/{id}")]
    [InlineData("/a/{id}/b")]
    [InlineData("/{first}/{second}")]
    [InlineData("/a/{first}/b/{second}")]
    [InlineData("/a//b")]
    [InlineData("/well-known/health.json")]
    [InlineData("/a%20b")]
    [InlineData("/Orders/Summary")]
    [InlineData("/one/two/three/four/five/six")]
    public void EveryRouteShapeCompiles(string route) {
        CompileApplication($$"""
            public class ShapeController {
                [Get("{{route}}")]
                public string Handle() => "x";
            }
            """);
    }

    /// <summary>
    /// A verb attribute with no argument at all. The path defaults to <c>"/"</c> inside the
    /// generator rather than to the attribute's own <c>path = ""</c> default, which is the only
    /// reason it produces a usable route.
    /// </summary>
    [Fact]
    public void AVerbAttributeWithNoRouteArgumentCompiles() {
        CompileApplication("""
            public class RootController {
                [Get]
                public string Root() => "root";
            }
            """);
    }

    /// <summary>
    /// A route declared through a constant rather than a literal. The path is read from the
    /// semantic model's constant value, so a <c>const</c> and a concatenation of two of them have
    /// to resolve the same way a literal does.
    /// </summary>
    [Fact]
    public void ARouteDeclaredThroughAConstantCompiles() {
        CompileApplication("""
            public static class Routes {
                public const string Prefix = "/orders";
                public const string List = Prefix + "/list";
            }

            public class OrderController {
                [Get(Routes.List)]
                public string List() => "orders";
            }
            """);
    }

    /// <summary>
    /// Routes whose tree nodes reduce to the same generated method name. <c>/a/x</c> and
    /// <c>/b/x</c> both produce a node with no remaining path and a node with the path <c>/x</c>,
    /// so <c>GetRouteMethodName</c> has to disambiguate or the second overwrites the first.
    /// </summary>
    [Fact]
    public void RoutesThatReduceToTheSameGeneratedMethodNameCompile() {
        CompileApplication("""
            public class SplitController {
                [Get("/a/x")]
                public string AX() => "ax";

                [Get("/b/x")]
                public string BX() => "bx";
            }
            """);
    }

    /// <summary>
    /// A deep tree with shared prefixes at several depths, literals competing with tokens, and a
    /// token whose suffix differs between routes. This is the shape that produces every kind of
    /// node the generator can emit in one file.
    /// </summary>
    [Fact]
    public void ALargeTreeWithSharedPrefixesAndTokensCompiles() {
        CompileApplication("""
            public class CatalogController {
                [Get("/order")]
                public string Order() => "order";

                [Get("/orders")]
                public string Orders() => "orders";

                [Get("/orderable")]
                public string Orderable() => "orderable";

                [Get("/organisation")]
                public string Organisation() => "organisation";

                [Get("/orders/{id}")]
                public string OrderById(string id) => id;

                [Get("/orders/{id}.json")]
                public string OrderJson(string id) => id;

                [Get("/orders/{id}/lines/{line}")]
                public string OrderLine(string id, string line) => id + line;

                [Get("/orders/active")]
                public string Active() => "active";

                [Post("/orders")]
                public string Create() => "created";

                [Delete("/orders/{id}")]
                public string Remove(string id) => id;
            }
            """);
    }

    /// <summary>
    /// Routes on separate controllers reach one table, and each controller is registered exactly
    /// once in the emitted dependency-injection method however many routes it declares.
    /// </summary>
    [Fact]
    public void RoutesSpreadAcrossControllersCompileIntoOneTable() {
        CompileApplication("""
            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";

                [Get("/orders/{id}")]
                public string One(string id) => id;
            }

            public class CustomerController {
                [Get("/customers")]
                public string List() => "customers";
            }

            public class HealthController {
                [Get("/health")]
                public string Health() => "ok";
            }
            """);
    }

    /// <summary>
    /// Two handlers with the same method name on one controller. The invoke class name carries a
    /// hash of the parameter names, so the overloads have to hash apart or the generator emits the
    /// same class twice.
    /// </summary>
    [Fact]
    public void OverloadedHandlerMethodsCompileToDistinctInvokeClasses() {
        var result = CompileApplication("""
            public class OverloadController {
                [Get("/one/{id}")]
                public string Handle(string id) => id;

                [Get("/two/{id}/{name}")]
                public string Handle(string id, string name) => id + name;
            }
            """);

        var invokeClasses = result.GeneratedSources.Keys
            .Where(name => name.StartsWith("OverloadController_Handle", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, invokeClasses.Length);
    }

    /// <summary>The same method name on two controllers keeps them apart by controller name.</summary>
    [Fact]
    public void TheSameMethodNameOnTwoControllersCompiles() {
        CompileApplication("""
            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";
            }

            public class CustomerController {
                [Get("/customers")]
                public string List() => "customers";
            }
            """);
    }

    [Theory]
    [InlineData("public void Handle() { }")]
    [InlineData("public string Handle() => \"x\";")]
    [InlineData("public int Handle() => 1;")]
    [InlineData("public Task Handle() => Task.CompletedTask;")]
    [InlineData("public Task<string> Handle() => Task.FromResult(\"x\");")]
    [InlineData("public ValueTask<string> Handle() => new ValueTask<string>(\"x\");")]
    [InlineData("public async Task<string> Handle() { await Task.Yield(); return \"x\"; }")]
    [InlineData("public async Task Handle() { await Task.Yield(); }")]
    public void EveryHandlerReturnShapeCompilesInsideARoutingTable(string handler) {
        CompileApplication($$"""
            public class ShapeController {
                [Get("/shape")]
                {{handler}}
            }
            """);
    }

    [Theory]
    [InlineData("string id", "/bind/{id}")]
    [InlineData("[FromQueryString] string filter", "/bind")]
    [InlineData("[FromQueryString(\"q\")] string term", "/bind")]
    [InlineData("[FromHeader(\"X-Tenant\")] string tenant", "/bind")]
    [InlineData("[FromServices] ITenantService service", "/bind")]
    [InlineData("[FromQueryString] int page = 1", "/bind")]
    [InlineData("[FromQueryString] string? optional", "/bind")]
    public void EveryBindingShapeCompilesInsideARoutingTable(string parameter, string route) {
        CompileApplication($$"""
            public interface ITenantService { }

            public class BindController {
                [Get("{{route}}")]
                public string Handle({{parameter}}) => "x";
            }
            """);
    }

    /// <summary>
    /// Every binding source at once, on a route that also carries a path token, inside a routing
    /// table. The parameter array and the metadata array are separate constructor arguments in
    /// adjacent positions, which is what a handler carrying both has to keep straight.
    /// </summary>
    [Fact]
    public void AllBindingSourcesAndMetadataInOneHandlerCompile() {
        CompileApplication("""
            public record OrderModel(string Sku);

            public interface ITenantService { }

            public class MixedController {
                [Post("/orders/{id}")]
                [Retry(Retries = 2)]
                [CacheControl(MaxAge = 60)]
                public string Mixed(
                    string id,
                    [FromQueryString] string filter,
                    [FromHeader("X-Tenant")] string tenant,
                    [FromServices] ITenantService service,
                    [FromBody] OrderModel model) => id + filter + tenant + model.Sku;
            }
            """);
    }

    [Theory]
    [InlineData("[CacheControl(MaxAge = 86400)]")]
    [InlineData("[CacheControl(Type = global::Hardened.Web.Runtime.CacheControl.CacheControlEnum.NoStore)]")]
    [InlineData("[RawResponse]")]
    [InlineData("[RawResponse(\"text/csv\")]")]
    [InlineData("[Template(\"home\")]")]
    public void EveryHandlerOptionAttributeCompiles(string attribute) {
        CompileApplication($$"""
            public class OptionController {
                [Get("/option")]
                {{attribute}}
                public string Handle() => "x";
            }
            """);
    }


    /// <summary>
    /// <c>[CacheControl]</c> declared on the controller rather than the handler. Class-level
    /// filter attributes are collected for every route on the class, so the metadata is emitted
    /// once per handler.
    /// </summary>
    [Fact]
    public void AControllerLevelHandlerOptionCompilesOnEveryRoute() {
        CompileApplication("""
            [CacheControl(MaxAge = 60)]
            public class AssetController {
                [Get("/one")]
                public string One() => "one";

                [Get("/two")]
                public string Two() => "two";
            }
            """);
    }

    /// <summary>
    /// An attribute the generator has never heard of. Anything that is not a verb,
    /// <c>[Template]</c> or <c>[RawResponse]</c> is treated as a filter attribute and copied into
    /// the handler's metadata verbatim — which means an ordinary framework attribute on a handler
    /// has to survive the trip into a file carrying none of the consumer's usings.
    /// </summary>
    [Fact]
    public void AnUnrecognisedAttributeOnAHandlerBecomesMetadataAndCompiles() {
        CompileApplication("""
            public class LegacyController {
                [Get("/legacy")]
                [System.Obsolete("superseded by /v2/legacy")]
                public string Legacy() => "legacy";
            }
            """);
    }

    /// <summary>
    /// A handler with metadata and no parameters at all. Before the 2026-08-11 generator fix this
    /// shape put the metadata array in the parameters slot and emitted C# that did not compile;
    /// <c>GeneratedCodeCompilesTests</c> pins the handler class, this pins it inside a routing
    /// table, where the handler is also constructed and registered.
    /// </summary>
    [Fact]
    public void AHandlerWithMetadataAndNoParametersCompilesInsideARoutingTable() {
        CompileApplication("""
            public class HealthController {
                [Get("/health")]
                [Retry(Retries = 2)]
                public string Health() => "ok";
            }
            """);
    }

    /// <summary>
    /// An assembly with an entry point and no routes at all. The routing table is still emitted,
    /// still implements the provider interface, and still has to compile — a static-content-only
    /// web application is exactly this shape.
    /// </summary>
    [Fact]
    public void AnApplicationWithNoRoutesStillCompilesARoutingTable() {
        CompileApplication("""
            public class NotAController {
                public string Value => "x";
            }
            """);
    }

    /// <summary>
    /// A controller declaring four routes is registered once, not four times. The generator walks
    /// the handler models to build the registration method, and every one of them names the same
    /// controller — so without the distinct pass the container holds four identical descriptors,
    /// and anything resolving the controller as a collection gets four instances.
    /// </summary>
    [Fact]
    public void AControllerIsRegisteredOnceHoweverManyRoutesItDeclares() {
        var result = CompileApplication("""
            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";

                [Post("/orders")]
                public string Create() => "created";

                [Get("/orders/{id}")]
                public string One(string id) => id;

                [Delete("/orders/{id}")]
                public string Remove(string id) => id;
            }
            """);

        var routing = result.SourceContaining("TestApplication.Routing");

        Assert.Equal(1, Occurrences(routing, "AddTransient<OrderController>"));
    }

    /// <summary>
    /// The routing table registers itself as the provider interface the web handler service asks
    /// for. Registering the concrete type instead leaves the service with no providers at all, and
    /// every route 404s into static content.
    /// </summary>
    [Fact]
    public void TheRoutingTableRegistersItselfAsTheHandlerProvider() {
        var result = CompileApplication("""
            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";
            }
            """);

        var routing = result.SourceContaining("TestApplication.Routing");

        // The registration is emitted across several lines, so the two type arguments are asserted
        // rather than the formatting between them.
        Assert.Contains("AddSingleton<", routing);
        Assert.Contains("IWebExecutionRequestHandlerProvider,", routing);
        Assert.Contains("TestApplication.RoutingTable", routing);
    }

    /// <summary>
    /// A controller is not required to be only handlers. Every method in the compilation is
    /// offered to the verb check, so an ordinary helper, a method carrying an unrelated attribute,
    /// and a private method all have to be passed over rather than routed or crashed on.
    /// </summary>
    [Fact]
    public void MethodsWithoutAVerbAttributeAreNotRoutes() {
        var result = CompileApplication("""
            public class MixedController {
                [Get("/orders")]
                public string List() => Format("orders");

                public string Format(string value) => value.ToUpperInvariant();

                [System.Obsolete("internal")]
                public string Legacy() => "legacy";

                private string Hidden() => "hidden";
            }
            """);

        Assert.Contains("MixedController_List.cs", result.GeneratedSources.Keys);

        Assert.DoesNotContain(result.GeneratedSources.Keys, name =>
            name.Contains("Format", StringComparison.Ordinal) ||
            name.Contains("Legacy", StringComparison.Ordinal) ||
            name.Contains("Hidden", StringComparison.Ordinal));
    }

    /// <summary>
    /// A parameter carrying an attribute the web generator does not recognise falls through to the
    /// custom-binding path, which passes the attribute itself into the emitted parameter
    /// information. That is the seam a consumer's own binding attribute arrives through, so it has
    /// to produce code that compiles rather than an unresolved type name.
    /// </summary>
    [Fact]
    public void AParameterCarryingAnUnrecognisedAttributeCompiles() {
        CompileApplication("""
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public class FromTenantAttribute : System.Attribute { }

            public class TenantController {
                [Get("/tenant")]
                public string Handle([FromTenant] string tenant) => tenant;
            }
            """);
    }

    private static int Occurrences(string source, string value) {
        var count = 0;
        var index = source.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0) {
            count++;
            index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
