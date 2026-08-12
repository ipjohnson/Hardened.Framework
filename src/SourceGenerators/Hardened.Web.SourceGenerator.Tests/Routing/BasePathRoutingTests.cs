using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// <c>[BasePath]</c> is read in two independent places, and they compose.
///
/// <para>
/// On a controller it is read by <c>WebRequestHandlerModelGenerator</c> and folded into the
/// handler's own path. On the <c>[HardenedModule]</c> entry point it is read by
/// <c>RoutingTableGenerator</c> and prefixed onto every route in the assembly. Neither knows about
/// the other, so a route under both is only reachable at the concatenation of the two — which is
/// the case a test is actually needed for.
/// </para>
/// </summary>
public class BasePathRoutingTests {

    [Fact]
    public void ARouteWithNoBasePathIsReachableAtItsOwnPath() {
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

        Assert.Equal("List", routing.Handler("GET", "/orders").InvokeMethod);
    }

    [Fact]
    public void ABasePathOnTheControllerPrefixesEveryRouteOnIt() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            [BasePath("/api")]
            public class OrderController {
                [Get("/orders")]
                public string List() => "orders";

                [Delete("/orders/{id}")]
                public string Remove(string id) => id;
            }
            """);

        Assert.Equal("List", routing.Handler("GET", "/api/orders").InvokeMethod);
        Assert.Equal("Remove", routing.Handler("DELETE", "/api/orders/7").InvokeMethod);

        // Reachable only under the base path, never at the bare route.
        Assert.Null(routing.Route("GET", "/orders"));
    }

    /// <summary>
    /// The assembly-wide base path. It goes on the <c>[HardenedModule]</c> entry point, which is the
    /// one class per assembly the generator treats as the application — see
    /// <c>Hardened.IntegrationTests.Web.SUT/WebLibrary.cs</c>, which declares
    /// <c>[BasePath("/web-library")]</c> exactly this way.
    /// </summary>
    [Fact]
    public void ABasePathOnTheModuleEntryPointPrefixesEveryRouteInTheAssembly() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            [BasePath("/module")]
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

        Assert.Equal("OrderController", routing.Handler("GET", "/module/orders").HandlerType.Name);
        Assert.Equal("CustomerController", routing.Handler("GET", "/module/customers").HandlerType.Name);

        Assert.Null(routing.Route("GET", "/orders"));
        Assert.Null(routing.Route("GET", "/customers"));
    }

    /// <summary>
    /// Both at once. The module's base path comes first, then the controller's, then the route —
    /// and a request at either of the two partial prefixes finds nothing.
    /// </summary>
    [Fact]
    public void ModuleAndControllerBasePathsCompose() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            [BasePath("/module")]
            public partial class TestApplication { }

            [BasePath("/api")]
            public class OrderController {
                [Get("/orders/{id}")]
                public string GetOrder(string id) => id;
            }
            """);

        Assert.Equal("GetOrder", routing.Handler("GET", "/module/api/orders/7").InvokeMethod);

        Assert.Null(routing.Route("GET", "/api/orders/7"));
        Assert.Null(routing.Route("GET", "/module/orders/7"));
    }

    /// <summary>
    /// Two controllers under different base paths keep the same route name apart. Without the
    /// prefix they would be one route declared twice, which the generator has no way to reconcile.
    /// </summary>
    [Fact]
    public void TheSameRouteUnderTwoBasePathsReachesTwoHandlers() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            [BasePath("/v1")]
            public class V1Controller {
                [Get("/items")]
                public string List() => "v1";
            }

            [BasePath("/v2")]
            public class V2Controller {
                [Get("/items")]
                public string List() => "v2";
            }
            """);

        Assert.Equal("V1Controller", routing.Handler("GET", "/v1/items").HandlerType.Name);
        Assert.Equal("V2Controller", routing.Handler("GET", "/v2/items").HandlerType.Name);
    }

    /// <summary>
    /// A controller base path reaches the handler's own metadata, which is what a filter reading
    /// <c>IExecutionRequestHandlerInfo.Path</c> sees.
    /// </summary>
    [Fact]
    public void AControllerBasePathIsPartOfTheHandlersDeclaredPath() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            [BasePath("/api")]
            public class OrderController {
                [Get("/orders/{id}")]
                public string GetOrder(string id) => id;
            }
            """);

        Assert.Equal("/api/orders/{id}", routing.Handler("GET", "/api/orders/7").Path);
    }
}
