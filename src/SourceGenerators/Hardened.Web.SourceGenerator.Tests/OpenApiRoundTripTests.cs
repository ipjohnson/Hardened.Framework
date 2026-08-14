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
