using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Web.Runtime.Routing;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Routing;

/// <summary>
/// What a registration callback is told when it gets a route wrong.
/// </summary>
/// <remarks>
/// These are the checks that used to be build errors and cannot be, because the path does not exist
/// until the application runs. They are made as early as they can be instead - before the first
/// request rather than as a 404 in production - and all of them are reported at once.
/// </remarks>
public class RouteRegistryTests
{
    [Fact]
    public void ARegisteredRouteAnswersAtTheComputedPath()
    {
        var registry = new RouteRegistry(Provider, Catalog());

        registry.Get("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));

        Assert.NotNull(registry.Close().Match("/acme/orders/7", "GET"));
    }

    [Fact]
    public void TheBasePathIsComposedOnAsItIsForAnAttributeRoute()
    {
        var registry = new RouteRegistry(Provider, Catalog(basePath: "/catalog"));

        registry.Get("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));

        var table = registry.Close();

        Assert.NotNull(table.Match("/catalog/acme/orders/7", "GET"));
        Assert.Null(table.Match("/acme/orders/7", "GET"));
    }

    [Fact]
    public void TheEntryPointsCaseRuleApplies()
    {
        var registry = new RouteRegistry(Provider, Catalog(caseInsensitive: true));

        registry.Get("/Acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));

        Assert.NotNull(registry.Close().Match("/acme/orders/7", "GET"));
    }

    [Fact]
    public void ADeclaredConstraintIsReachable()
    {
        var registry = new RouteRegistry(
            Provider,
            Catalog(
                constraints: new Dictionary<string, RouteConstraintTest>
                {
                    { "isbn", static value => value.Length == 13 },
                }
            )
        );

        registry.Map("GET", "/books/{id:isbn}", typeof(Orders), nameof(Orders.Get));

        Assert.Empty(registry.Failures);
    }

    /// <remarks>
    /// The check §8 calls the one most likely to bite. The handler binds the token called
    /// <c>id</c>; registered under a template that spells it <c>orderId</c> it would bind nothing
    /// and answer 400 to every request.
    /// </remarks>
    [Fact]
    public void ATemplateThatRenamesTheTokenIsReported()
    {
        var registry = new RouteRegistry(Provider, Catalog());

        registry.Get("/acme/orders/{orderId:int}", typeof(Orders), nameof(Orders.Get));

        Assert.Contains("does not declare the token '{id}'", Assert.Single(registry.Failures));
    }

    [Fact]
    public void ATemplateWithNoTokenAtAllIsReported()
    {
        var registry = new RouteRegistry(Provider, Catalog());

        registry.Get("/acme/orders", typeof(Orders), nameof(Orders.Get));

        Assert.Contains("does not declare the token '{id}'", Assert.Single(registry.Failures));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public void EveryVerbReachesTheSameRegistration(string verb)
    {
        var registry = new RouteRegistry(Provider, Catalog(verb));

        switch (verb)
        {
            case "PUT":
                registry.Put("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));
                break;
            case "PATCH":
                registry.Patch("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));
                break;
            case "DELETE":
                registry.Delete("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));
                break;
            default:
                registry.Post("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));
                break;
        }

        Assert.Empty(registry.Failures);
        Assert.NotNull(registry.Close().Match("/acme/orders/7", verb));
    }

    /// <remarks>
    /// A base path of "/" is the root, and composing it would double the separator.
    /// </remarks>
    [Fact]
    public void ARootBasePathComposesToNothing()
    {
        var registry = new RouteRegistry(Provider, Catalog(basePath: "/"));

        registry.Get("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));

        Assert.NotNull(registry.Close().Match("/acme/orders/7", "GET"));
    }

    /// <remarks>
    /// Unreachable short of a generator defect, since the declared path came from an attribute the
    /// generator already accepted. Reported rather than assumed away.
    /// </remarks>
    [Fact]
    public void AHandlerDeclaredWithSomethingThatIsNotARouteIsReported()
    {
        var registry = new RouteRegistry(Provider, Catalog(declaredPath: "orders/{id:int}"));

        registry.Get("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));

        Assert.Contains("is not a route template", Assert.Single(registry.Failures));
    }

    /// <remarks>
    /// The builder finds this rather than the registry, and the registry has to carry the message
    /// out with the rest of them rather than throwing where it is found.
    /// </remarks>
    [Fact]
    public void TheSameRouteRegisteredTwiceIsReported()
    {
        var registry = new RouteRegistry(Provider, Catalog());

        registry.Get("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));
        registry.Get("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));

        Assert.Contains("could never be reached", Assert.Single(registry.Failures));
    }

    [Fact]
    public void AHandlerNobodyGeneratedIsReported()
    {
        var registry = new RouteRegistry(Provider, Catalog());

        registry.Get("/acme/nothing", typeof(Orders), "Missing");

        Assert.Contains("which is not a handler", Assert.Single(registry.Failures));
    }

    [Fact]
    public void AHandlerRegisteredUnderAnotherVerbIsReported()
    {
        var registry = new RouteRegistry(Provider, Catalog());

        registry.Post("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));

        Assert.Contains("it is declared GET", Assert.Single(registry.Failures));
    }

    [Fact]
    public void AnApplicationWithNoCatalogIsToldWhy()
    {
        var registry = new RouteRegistry(Provider, null);

        registry.Get("/acme/orders/{id:int}", typeof(Orders), nameof(Orders.Get));

        Assert.Contains("generated no handler catalog", Assert.Single(registry.Failures));
    }

    /// <remarks>
    /// All of them, and that is the point. Throwing on the first would make a fifty-route
    /// registration a fifty-restart debugging session.
    /// </remarks>
    [Fact]
    public void EveryFailureIsThrownAtOnce()
    {
        var registry = new RouteRegistry(Provider, Catalog());

        registry.Get("/one/orders/{orderId:int}", typeof(Orders), nameof(Orders.Get));
        registry.Get("/two/orders/{orderId:int}", typeof(Orders), nameof(Orders.Get));
        registry.Get("three", typeof(Orders), nameof(Orders.Get));

        var thrown = Assert.Throws<RouteRegistrationException>(() => registry.Close());

        Assert.Equal(3, thrown.Failures.Count);
        Assert.Contains("3 routes could not be registered", thrown.Message);
    }

    [Fact]
    public void RegisteringAfterStartupThrows()
    {
        var registry = new RouteRegistry(Provider, Catalog());

        registry.Close();

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            registry.Get("/late", typeof(Orders), nameof(Orders.Get))
        );

        Assert.Contains("cannot be registered after startup", thrown.Message);
    }

    // ---- helpers ------------------------------------------------------------

    private static readonly IServiceProvider Provider = new EmptyProvider();

    private static IGeneratedRouteHandlerCatalog Catalog(
        string verb = "GET",
        bool caseInsensitive = false,
        string basePath = "",
        string declaredPath = "/orders/{id:int}",
        IReadOnlyDictionary<string, RouteConstraintTest>? constraints = null
    ) =>
        new StubCatalog(
            verb,
            caseInsensitive,
            basePath,
            declaredPath,
            constraints ?? new Dictionary<string, RouteConstraintTest>()
        );

    /// <summary>Stands in for the class the generator emits beside the routing table.</summary>
    private sealed class StubCatalog : IGeneratedRouteHandlerCatalog
    {
        public StubCatalog(
            string verb,
            bool caseInsensitive,
            string basePath,
            string declaredPath,
            IReadOnlyDictionary<string, RouteConstraintTest> constraints
        )
        {
            CaseInsensitiveRoutes = caseInsensitive;
            BasePath = basePath;
            Constraints = constraints;
            Handlers =
            [
                new GeneratedRouteHandler(
                    typeof(Orders),
                    nameof(Orders.Get),
                    verb,
                    declaredPath,
                    static (_, routePath) => new StubHandler(routePath ?? "")
                ),
            ];
        }

        public IReadOnlyList<GeneratedRouteHandler> Handlers { get; }

        public bool CaseInsensitiveRoutes { get; }

        public string BasePath { get; }

        public IReadOnlyDictionary<string, RouteConstraintTest> Constraints { get; }
    }

    private class Orders
    {
        public string Get(int id) => id.ToString();
    }

    private sealed class StubHandler : IExecutionRequestHandler
    {
        public StubHandler(string path)
        {
            HandlerInfo = new ExecutionRequestHandlerInfo(
                path,
                "GET",
                typeof(Orders),
                nameof(Orders.Get)
            );
        }

        public IExecutionRequestHandlerInfo HandlerInfo { get; }

        public IExecutionChain GetExecutionChain(IExecutionContext context) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
