using System.Text.RegularExpressions;
using Hardened.OpenApi.SourceGenerator;
using Hardened.Generation.Models;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.SourceGenerator.Tests.Routing;
using Xunit;
using Hardened.Idl;
using Hardened.Generation;

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
        [Hardened.Shared.Runtime.Attributes.Enable<Hardened.Web.Runtime.OpenApi.OpenApiDocumentPublishing>]
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

        [BasePath("/feeds")]
        public class FeedController {

            [Get("/ndjson")]
            public async IAsyncEnumerable<Product> Ndjson() {
                await System.Threading.Tasks.Task.Yield();

                yield return new Product();
            }

            [Get("/events")]
            [ServerSentEvents]
            public async IAsyncEnumerable<Product> Events() {
                await System.Threading.Tasks.Task.Yield();

                yield return new Product();
            }
        }
        """;

    /// <summary>
    /// The document the web generator emits, parsed by the build task exactly as it would parse a
    /// hand-written specification.
    /// </summary>
    private static ServiceSpecModel Reparsed() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Application },
            new[] { new WebLibrarySourceGenerator() },
            GeneratedRoutingTable.Anchors);

        result.AssertNoErrors();

        var source = result.SourceContaining("OpenApiDocument");

        var model = OpenApiSpecParser.Parse(
            GeneratedOpenApiDocument.Extract(source), "round-trip.json", CancellationToken.None);

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

        Assert.Equal(new[] { "ICartService", "IFeedService", "IProductService" }, interfaces);
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
                "GET /baskets/{id}", "GET /feeds/events", "GET /feeds/ndjson",
                "GET /products", "GET /products/{id}", "POST /baskets"
            },
            paths);
    }


    /// <summary>
    /// A streamed handler comes back as a stream, not as one of what it streams.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the assertion the whole streaming-document change exists to satisfy.</b> The two
    /// halves are separately plausible and only useful together: the emitter writes
    /// <c>itemSchema</c> under the framing's media type, and the specification-first reader has to
    /// turn that back into <c>IAsyncEnumerable&lt;T&gt;</c>. Ship one without the other and the
    /// document says something nothing reads, or the reader looks for something nothing writes.
    /// </para>
    /// <para>
    /// Before <c>itemSchema</c> the emitter put the item's schema under <c>schema</c>, which claims
    /// the response <em>is</em> one item. That round-tripped without complaint and produced
    /// <c>Task&lt;Product&gt;</c> - a client that reads one product and stops, from a route that
    /// streams them. Nothing errored; the interface was simply wrong, which is the failure this
    /// file was written for.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStreamedHandlerComesBackAsAStream() {
        var operations = Reparsed().Services
            .SelectMany(service => service.Operations)
            .Where(operation => operation.Path.StartsWith("/feeds"))
            .ToDictionary(operation => operation.Path);

        Assert.Equal(2, operations.Count);

        foreach (var operation in operations.Values) {
            Assert.NotNull(operation.ItemSchemaRef);
            Assert.Contains("Product", operation.ItemSchemaRef!);

            // The item schema is what a stream has instead of a response schema, not as well as.
            Assert.Null(operation.ResponseRef);
        }
    }

    /// <summary>
    /// The framing survives as the media type, so the two streams are distinguishable.
    /// </summary>
    /// <remarks>
    /// A document that described both as the same content type would generate one client for two
    /// wire formats, and the one that got it wrong would fail on the first byte.
    /// </remarks>
    [Fact]
    public void TheFramingSurvivesAsTheMediaType() {
        var operations = Reparsed().Services
            .SelectMany(service => service.Operations)
            .Where(operation => operation.Path.StartsWith("/feeds"))
            .ToDictionary(operation => operation.Path);

        Assert.Equal("application/x-ndjson", operations["/feeds/ndjson"].ResponseContentType);
        Assert.Equal("text/event-stream", operations["/feeds/events"].ResponseContentType);
    }
}
