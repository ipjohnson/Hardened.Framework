using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// The catalog a route registered at startup resolves its handler through.
/// </summary>
/// <remarks>
/// <para>
/// The split the feature rests on: the route is data and the handler is compiled code. What is
/// emitted here is a factory per handler keyed by the controller method it came from, plus the
/// three build-time facts a runtime table cannot read anywhere else - the case rule, the base
/// path, and the constraints the application declared.
/// </para>
/// <para>
/// The gate matters as much as the contents. An application that registers nothing at run time has
/// to generate exactly what it generated before this existed, which is what keeps every checked-in
/// routing fixture and every existing application unchanged.
/// </para>
/// </remarks>
public class RegisteredRouteCatalogTests
{
    private static readonly Type[] Anchors = [typeof(GetAttribute), typeof(FromBodyAttribute)];

    private const string Registration = """
        public class TenantRoutes : IRouteRegistration {
            public ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken) {
                routes.Get("/acme/items/{id:int}", typeof(ItemController), nameof(ItemController.Item));

                return default;
            }
        }
        """;

    private static string Application(
        string extra,
        string moduleAttributes = "",
        string constraints = ""
    ) =>
        $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Routing;

            namespace TestApp;

            [HardenedModule]
            {{moduleAttributes}}
            public partial class TestApplication { }

            {{constraints}}

            public class ItemController {
                [Get("/items/{id:int}")]
                public string Item(int id) => id.ToString();
            }

            {{extra}}
            """;

    private static GeneratorResult Generate(
        string extra,
        string moduleAttributes = "",
        string constraints = ""
    ) =>
        GeneratorTestHarness
            .Run(
                new Dictionary<string, string>
                {
                    ["Test.cs"] = Application(extra, moduleAttributes, constraints),
                },
                new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
                Anchors
            )
            .AssertNoErrors();

    private static string Catalog(
        string extra,
        string moduleAttributes = "",
        string constraints = ""
    ) => Generate(extra, moduleAttributes, constraints).SourceContaining("RouteHandlers");

    [Fact]
    public void AnApplicationThatRegistersNothingEmitsNoCatalog()
    {
        var result = Generate("");

        Assert.DoesNotContain(
            result.GeneratedSources.Keys,
            key => key.Contains("RouteHandlers", StringComparison.Ordinal)
        );
        Assert.DoesNotContain("RegisteredRouteHandlers", result.SourceContaining("Routing"));
    }

    [Fact]
    public void DeclaringARegistrationEmitsTheCatalog()
    {
        var catalog = Catalog(Registration);

        Assert.Contains(
            "class RegisteredRouteHandlers : global::Hardened.Web.Runtime.Routing.IGeneratedRouteHandlerCatalog",
            catalog
        );
    }

    /// <remarks>
    /// The routing table's own dependency method, so the catalog is registered by the same call
    /// that registers the table rather than by a second one an application has to remember.
    /// </remarks>
    [Fact]
    public void TheCatalogIsRegisteredWithTheRoutingTable()
    {
        var routing = Generate(Registration).SourceContaining("Routing");

        Assert.Contains(
            "AddSingleton<global::Hardened.Web.Runtime.Routing.IGeneratedRouteHandlerCatalog, TestApp.TestApplication.RegisteredRouteHandlers>()",
            routing
        );
    }

    /// <remarks>
    /// The declared route, not the composed one. The registry reads it for its token names, and the
    /// base path contributes none.
    /// </remarks>
    [Fact]
    public void TheCatalogCarriesTheHandlerItWasDeclaredWith()
    {
        var routing = Catalog(Registration);

        Assert.Contains("typeof(global::TestApp.ItemController), \"Item\", \"GET\"", routing);
        Assert.Contains("\"/items/{id:int}\"", routing);
        Assert.Contains(
            "static (serviceProvider, routePath) => new global::TestApp.Generated.",
            routing
        );
    }

    [Fact]
    public void TheCatalogCarriesTheEntryPointsCaseRuleAndBasePath()
    {
        var routing = Catalog(Registration, "[CaseInsensitiveRoutes]\n[BasePath(\"/catalog\")]");

        Assert.Contains("public bool CaseInsensitiveRoutes => true;", routing);
        Assert.Contains("public string BasePath => \"/catalog\";", routing);
    }

    [Fact]
    public void AnEntryPointWithNeitherSaysSo()
    {
        var routing = Catalog(Registration);

        Assert.Contains("public bool CaseInsensitiveRoutes => false;", routing);
        Assert.Contains("public string BasePath => \"\";", routing);
    }

    /// <remarks>
    /// A constraint still has to be known at compile time to be compiled in. This is the same
    /// static method the generated table calls directly, handed over as a delegate so a template
    /// that only exists at run time can name it.
    /// </remarks>
    [Fact]
    public void TheCatalogCarriesTheDeclaredRouteConstraints()
    {
        var routing = Catalog(
            Registration,
            constraints: """
            public static class Codes {
                [RouteConstraint("code")]
                public static bool IsCode(ReadOnlySpan<char> value) => value.Length == 3;
            }
            """
        );

        Assert.Contains("{ \"code\", global::TestApp.Codes.IsCode }", routing);
    }

    [Fact]
    public void AnApplicationDeclaringNoConstraintCarriesAnEmptyMap()
    {
        var routing = Catalog(Registration);

        Assert.Contains(
            "Dictionary<string, global::Hardened.Web.Runtime.Routing.RouteConstraintTest>()",
            routing
        );
    }
}
