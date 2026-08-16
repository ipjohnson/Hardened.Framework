using System.Text.RegularExpressions;
using Hardened.OpenApi.SourceGenerator;
using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.SourceGenerator.Tests.Routing;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// Attribute routes out to a document, and that document back in through the build task that
/// compiles a specification.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpenApiRoundTripTests"/> asserts the document is valid and says the right things.
/// This asserts the thing those properties exist for: that a specification-first build fed this
/// document reconstructs the application's shape. The two halves were only ever agreeing by
/// inspection before, and the failure they hide is silent - the emitter wrote no tags, so
/// everything grouped under <c>"Default"</c> and the whole controller structure became one
/// <c>IDefaultService</c>. Nothing errors; the interfaces are simply wrong.
/// </para>
/// <para>
/// This runs the real parser rather than a re-derivation of its rules. A test that reimplemented
/// <c>ToInterfaceName</c> to check <c>ToInterfaceName</c> would agree with itself no matter what
/// either side did.
/// </para>
/// </remarks>
public class OpenApiReverseRoundTripTests {

    private const string Application =
        """
        using System.Collections.Generic;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class Product {
            public string Sku { get; set; } = "";
        }

        [BasePath("/products")]
        public class ProductController {

            [Get("/{id}")]
            public Product Get(string id) => new();

            [Get("/")]
            public List<Product> List() => new();
        }

        [BasePath("/baskets")]
        [Tag("Cart")]
        public class BasketController {

            [Get("/{id}")]
            public Product Get(string id) => new();

            [Post("/")]
            public Product Add(Product product) => new();
        }
        """;

    /// <summary>
    /// The document the web generator emits, parsed by the build task exactly as it would parse a
    /// hand-written specification.
    /// </summary>
    private static OpenApiSpecModel Reparsed() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Application },
            new[] { new WebLibrarySourceGenerator() },
            GeneratedRoutingTable.Anchors);

        result.AssertNoErrors();

        var source = result.SourceContaining("OpenApiDocument");
        var match = Regex.Match(source, "\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Singleline);

        Assert.True(match.Success, "No document literal in the generated source.");

        var model = OpenApiSpecParser.Parse(
            Regex.Unescape(match.Groups[1].Value), "round-trip.json", CancellationToken.None);

        Assert.True(model != null, "The build task's reader rejected the emitted document.");

        return model!;
    }

    /// <summary>
    /// One service per controller, named after it. Before tags were emitted this produced a single
    /// service tagged <c>Default</c> holding every operation in the application.
    /// </summary>
    [Fact]
    public void EachControllerComesBackAsItsOwnService() {
        var interfaces = Reparsed().Services
            .Select(service => NamingHelper.ToInterfaceName(service.Tag))
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(new[] { "ICartService", "IProductService" }, interfaces);
    }

    /// <summary>
    /// And each operation comes back under the method name it was written as. That is what makes
    /// the emitted <c>operationId</c> camelCase: <c>ToMethodName</c> pascal-cases it, so the
    /// original name is what a specification-first build declares on the interface.
    /// </summary>
    [Fact]
    public void MethodNamesSurviveTheRoundTrip() {
        var products = Assert.Single(Reparsed().Services, service => service.Tag == "Product");

        var methods = products.Operations
            .Select(operation => NamingHelper.ToMethodName(operation.OperationId))
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(new[] { "List", "ProductGet" }, methods);
    }

    /// <summary>
    /// <c>Get</c> is declared on both controllers, so neither can keep the bare name and the tag
    /// disambiguates. The cost falls only on the operations that clashed — <c>List</c> and
    /// <c>Add</c> keep theirs.
    /// </summary>
    [Fact]
    public void OnlyTheClashingNamesAreQualified() {
        var cart = Assert.Single(Reparsed().Services, service => service.Tag == "Cart");

        var methods = cart.Operations
            .Select(operation => NamingHelper.ToMethodName(operation.OperationId))
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(new[] { "Add", "CartGet" }, methods);
    }

    /// <summary>
    /// Routes survive with their base path, since that is the path a client calls.
    /// </summary>
    /// <remarks>
    /// The collection routes are <c>/products</c> and <c>/baskets</c>, not <c>/products/</c> and
    /// <c>/baskets/</c>. They read the other way until the base path and a <c>/</c> template stopped
    /// being concatenated — which is the shape this test exists to protect, since a document is
    /// what a generated client calls and the trailing slash was a URL the application did not serve.
    /// </remarks>
    [Fact]
    public void RoutesSurviveTheRoundTrip() {
        var paths = Reparsed().Services
            .SelectMany(service => service.Operations)
            .Select(operation => operation.HttpMethod + " " + operation.Path)
            .OrderBy(route => route, StringComparer.Ordinal);

        Assert.Equal(
            new[] {
                "GET /baskets/{id}", "GET /products", "GET /products/{id}", "POST /baskets"
            },
            paths);
    }
}
