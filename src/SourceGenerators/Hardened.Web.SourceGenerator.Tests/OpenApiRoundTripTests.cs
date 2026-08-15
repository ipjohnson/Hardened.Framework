using System.Text.RegularExpressions;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.SourceGenerator.Tests.Routing;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// Attribute routes out to an OpenAPI document, and that document back in through a reader.
/// </summary>
/// <remarks>
/// <para>
/// The acceptance test for the reverse direction. Every other test here asserts on characters the
/// generator wrote, which proves it produced what somebody expected rather than that the result is
/// a document at all. This parses it with <c>Microsoft.OpenApi</c> - the same reader the
/// specification-first build task uses - so what is asserted is that a real OpenAPI parser agrees
/// the output describes the routes the application declares.
/// </para>
/// <para>
/// Deliberately not Hardened's own parser. A misunderstanding shared between the writer and the
/// parser would agree with itself and pass; an independent reader cannot be talked into it.
/// </para>
/// </remarks>
public class OpenApiRoundTripTests {

    private const string Application =
        """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class Order {
            public string Sku { get; set; } = "";
            public int Quantity { get; set; }
            public List<string>? Tags { get; set; }
        }

        public class OrderSummary {
            public string Id { get; set; } = "";
            public Order Order { get; set; } = new();
        }

        [BasePath("/orders")]
        public class OrderController {

            [Get("/{id}")]
            public OrderSummary Get(string id) => new();

            [Get("/")]
            public List<OrderSummary> List(
                [FromQueryString] int page,
                [FromHeader("X-Tenant")] string tenant) => new();

            [Post("/")]
            public OrderSummary Create(Order order) => new();

            [Delete("/{id}")]
            public void Delete(string id) { }
        }

        [BasePath("/customers")]
        [Tag("People")]
        public class CustomerController {

            [Get("/{id}")]
            public OrderSummary Get(string id) => new();

            [Get("/active")]
            public List<OrderSummary> Active() => new();
        }
        """;

    /// <summary>
    /// The document the generator emitted, read back with a real OpenAPI parser.
    /// </summary>
    private static OpenApiDocument RoundTrip() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Application },
            new[] { new WebLibrarySourceGenerator() },
            GeneratedRoutingTable.Anchors);

        result.AssertNoErrors();

        var document = ExtractDocument(result.SourceContaining("OpenApiDocument"));

        var parsed = new OpenApiStringReader().Read(document, out var diagnostic);

        // The build task refuses a specification its reader reports errors on, so a document that
        // fails here is one the specification-first direction would reject outright.
        Assert.Empty(diagnostic.Errors);

        return parsed;
    }

    /// <summary>The string literal the generator wrote, unescaped back to its JSON.</summary>
    private static string ExtractDocument(string source) {
        var match = Regex.Match(source, "\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Singleline);

        Assert.True(match.Success, "No document literal in the generated source.");

        return Regex.Unescape(match.Groups[1].Value);
    }

    [Fact]
    public void TheEmittedDocumentIsValidOpenApi() {
        var document = RoundTrip();

        Assert.Equal("TestApplication", document.Info.Title);
        Assert.NotEmpty(document.Paths);
    }

    /// <summary>
    /// Every route the application declares appears, under the path it is served from — base path
    /// included, since that is what a client would have to call.
    /// </summary>
    [Fact]
    public void EveryRouteAppears() {
        var document = RoundTrip();

        Assert.True(document.Paths.ContainsKey("/orders/{id}"), "the token route is missing");
        Assert.True(document.Paths.ContainsKey("/orders/"), "the collection route is missing");

        var byId = document.Paths["/orders/{id}"].Operations;

        Assert.True(byId.ContainsKey(OperationType.Get));
        Assert.True(byId.ContainsKey(OperationType.Delete));

        var collection = document.Paths["/orders/"].Operations;

        Assert.True(collection.ContainsKey(OperationType.Get));
        Assert.True(collection.ContainsKey(OperationType.Post));
    }

    /// <summary>
    /// Each parameter is described where it actually comes from. A path token reported as a query
    /// parameter would generate a client that cannot call the endpoint.
    /// </summary>
    [Fact]
    public void ParametersKeepTheirLocation() {
        var document = RoundTrip();

        var byId = document.Paths["/orders/{id}"].Operations[OperationType.Get];

        var id = Assert.Single(byId.Parameters);

        Assert.Equal("id", id.Name);
        Assert.Equal(ParameterLocation.Path, id.In);
        Assert.True(id.Required);

        var list = document.Paths["/orders/"].Operations[OperationType.Get];

        var locations = list.Parameters.ToDictionary(p => p.Name, p => p.In);

        Assert.Equal(ParameterLocation.Query, locations["page"]);
        Assert.Equal(ParameterLocation.Header, locations["X-Tenant"]);
    }

    /// <summary>
    /// A body is described by a schema referencing a real component - the part that needed the C#
    /// type walked while its Roslyn symbol still existed.
    /// </summary>
    [Fact]
    public void BodiesAreDescribedByResolvableSchemas() {
        var document = RoundTrip();

        var create = document.Paths["/orders/"].Operations[OperationType.Post];

        var request = create.RequestBody.Content["application/json"].Schema;

        // The reader resolves the $ref, so reaching the properties proves the component exists.
        Assert.Equal("object", request.Type);
        Assert.True(request.Properties.ContainsKey("sku"));
        Assert.Equal("integer", request.Properties["quantity"].Type);
        Assert.Equal("array", request.Properties["tags"].Type);
    }

    /// <summary>A type reached through another is written once and referenced.</summary>
    [Fact]
    public void NestedTypesBecomeTheirOwnComponents() {
        var document = RoundTrip();

        Assert.True(document.Components.Schemas.ContainsKey("Order"));
        Assert.True(document.Components.Schemas.ContainsKey("OrderSummary"));

        var summary = document.Components.Schemas["OrderSummary"];

        Assert.Equal("object", summary.Properties["order"].Type);
    }

    /// <summary>
    /// A handler returning nothing declares a response with no content, rather than a body of an
    /// unnameable type.
    /// </summary>
    [Fact]
    public void AVoidHandlerHasNoResponseBody() {
        var document = RoundTrip();

        var delete = document.Paths["/orders/{id}"].Operations[OperationType.Delete];

        Assert.Empty(delete.Responses["200"].Content);
    }

    /// <summary>All operations in the document, flattened out of the path grouping.</summary>
    private static IReadOnlyList<OpenApiOperation> Operations(OpenApiDocument document) =>
        document.Paths.Values.SelectMany(path => path.Operations.Values).ToArray();

    /// <summary>
    /// <c>operationId</c> MUST be unique - a client generator fed a document with a duplicate
    /// either fails or silently drops an operation.
    /// </summary>
    /// <remarks>
    /// It was not. The id was built from the verb and the route's literal segments with tokens
    /// skipped, so <c>/verbs/item</c> and <c>/verbs/item/{id}</c> collided. Verified on the WebApp
    /// fixture: 42 operations, <c>getBindingPath</c> and <c>deleteVerbsItem</c> emitted twice each.
    /// Every test in this file passed while that was true, because Microsoft.OpenApi is a lenient
    /// reader - which is exactly why this assertion is here and not left to the parser.
    /// </remarks>
    [Fact]
    public void EveryOperationIdIsUnique() {
        var ids = Operations(RoundTrip()).Select(operation => operation.OperationId).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// And is the C# method name, so a generated client reads like the application it was
    /// generated from. <c>NamingHelper.ToMethodName</c> pascal-cases the id, so the camelCase form
    /// is what comes back as the original name.
    /// </summary>
    [Fact]
    public void AnOperationIdIsTheMethodName() {
        var list = RoundTrip().Paths["/orders/"].Operations[OperationType.Get];

        Assert.Equal("list", list.OperationId);
    }

    /// <summary>
    /// Two controllers with a method of the same name cannot both keep it, so the tag - the one
    /// thing that tells them apart - disambiguates. Both <c>OrderController</c> and
    /// <c>CustomerController</c> declare <c>Get</c>.
    /// </summary>
    [Fact]
    public void ACrossControllerClashIsDisambiguatedByTheTag() {
        var document = RoundTrip();

        Assert.Equal("orderGet", document.Paths["/orders/{id}"].Operations[OperationType.Get].OperationId);
        Assert.Equal("peopleGet", document.Paths["/customers/{id}"].Operations[OperationType.Get].OperationId);
    }

    /// <summary>
    /// Every operation carries a tag. The emitter wrote none, and specification-first groups by
    /// <c>Tags.FirstOrDefault()?.Name ?? "Default"</c> - so a round-tripped application collapsed
    /// into a single <c>IDefaultService</c> and lost its controller structure entirely.
    /// </summary>
    [Fact]
    public void EveryOperationCarriesATag() {
        Assert.All(Operations(RoundTrip()), operation => Assert.NotEmpty(operation.Tags));
    }

    /// <summary>
    /// And the tag set is the controller set. No new grouping construct was introduced for this:
    /// the controller already is the group, and the document simply did not say so.
    /// <c>CustomerController</c> carries <c>[Tag("People")]</c>, which is the override.
    /// </summary>
    [Fact]
    public void TheTagSetIsTheControllerSet() {
        var tags = Operations(RoundTrip())
            .SelectMany(operation => operation.Tags.Select(tag => tag.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(new[] { "Order", "People" }, tags);
    }

    /// <summary>
    /// The same application produces the same document. The generator is an incremental one, and a
    /// document whose ordering wandered between runs would rewrite the file on every build.
    /// </summary>
    [Fact]
    public void TheDocumentIsStableAcrossRuns() {
        var first = RoundTrip();
        var second = RoundTrip();

        Assert.Equal(
            first.Paths.Keys.OrderBy(k => k, StringComparer.Ordinal),
            second.Paths.Keys.OrderBy(k => k, StringComparer.Ordinal));

        Assert.Equal(
            first.Components.Schemas.Keys.OrderBy(k => k, StringComparer.Ordinal),
            second.Components.Schemas.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }
}
