extern alias buildtask;

// The build task's copy of the shared generation model, reached through its alias: this
// project also links the web generator's copy, and both in the global namespace is CS0433.
using System.Text.RegularExpressions;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.SourceGenerator.Tests.Routing;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Xunit;
using buildtask::Hardened.Idl;

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
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;
        using ValidationModules.Constraints;

        namespace TestApp;

        [HardenedModule]
        [Hardened.Shared.Runtime.Attributes.Enable<Hardened.Web.Runtime.OpenApi.OpenApiDocumentPublishing>]
        [Server("https://api.example.com", "Production")]
        [Server("https://staging.example.com")]
        [OpenApiInfo("Orders API", "3.5.0")]
        public partial class TestApplication { }

        public enum Priority { Standard, Express, NextDay }

        public class Order {
            [StringLength(3, 12)]
            [Pattern("^[A-Z0-9-]+$")]
            public string Sku { get; set; } = "";

            [Range(1, 500)]
            public int Quantity { get; set; }

            [AllowedValues("standard", "express")]
            public string? Shipping { get; set; }

            [ItemCount(0, 8)]
            public List<string>? Tags { get; set; }

            public Priority Priority { get; set; }
        }

        public class OrderSummary {
            public string Id { get; set; } = "";
            public Order Order { get; set; } = new();
        }

        [BasePath("/orders")]
        public class OrderController {

            /// <summary>One order, by its identifier.</summary>
            /// <remarks>
            /// Reads from the replica, so an order created in the last few
            /// seconds may not be visible yet.
            /// </remarks>
            [Get("/{id}")]
            public OrderSummary Get(string id) => new();

            [Get("/")]
            public List<OrderSummary> List(
                [FromQueryString] int page,
                [FromHeader("X-Tenant")] string tenant,
                [FromQueryString] Priority? priority = null,
                [FromQueryString] int limit = 20) => new();

            [Post("/")]
            public OrderSummary Create(Order order) => new();

            [Obsolete("Cancel the order instead.")]
            [Delete("/{id}")]
            public void Delete(string id) { }

            /// <summary>Every scalar a value parsed from text can be declared as.</summary>
            [Get("/search/{count:long}")]
            public List<OrderSummary> Search(
                long count,
                [FromQueryString] bool includeCancelled,
                [FromQueryString] Guid tenant,
                [FromQueryString] DateTime placedAfter,
                [FromQueryString] decimal minimumTotal,
                [FromQueryString] double weighting,
                [FromHeader("X-Page")] int page) => new();

            [Get("/receipts/{*path}")]
            public string Receipt(string path) => path;

            [Get("/on/{day}")]
            public List<OrderSummary> OnDay(
                DateOnly day,
                [FromQueryString] Uri callback,
                [FromQueryString] float weight,
                [FromQueryString] short batch) => new();
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
    /// <summary>
    /// The routing anchors plus ValidationModules, whose constraints the fixture puts on the model
    /// so the schema facets they imply can be asserted. The validation generator itself is
    /// deliberately not run - the constraints are being read as documentation here, not compiled.
    /// </summary>
    private static readonly Type[] Anchors =
        GeneratedRoutingTable.Anchors.Append(typeof(ValidationModules.Constraints.RangeAttribute)).ToArray();

    private static OpenApiDocument RoundTrip() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Application },
            new[] { new WebLibrarySourceGenerator() },
            Anchors);

        result.AssertNoErrors();

        var document = GeneratedOpenApiDocument.Extract(result.SourceContaining("OpenApiDocument"));

        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        var read = OpenApiDocument.Parse(document, "yaml", settings);
        var parsed = read.Document;
        var diagnostic = read.Diagnostic!;

        // The build task refuses a specification its reader reports errors on, so a document that
        // fails here is one the specification-first direction would reject outright.
        Assert.Empty(diagnostic.Errors);

        Assert.NotNull(parsed);

        return parsed;
    }

    /// <summary>
    /// The operations declared on a path, asserted to be there.
    /// </summary>
    /// <remarks>
    /// Microsoft.OpenApi 3.x annotates most of a document as nullable, because most of a document
    /// is optional in the specification. Every use of it here is reading back a document this test
    /// just generated, so a null means the generator omitted something the assertion below was
    /// about to check - which is worth failing on by name rather than dereferencing and reporting a
    /// NullReferenceException from whichever line got there first.
    /// </remarks>
    private static IDictionary<HttpMethod, OpenApiOperation> Operations(
        OpenApiDocument document, string path) {
        Assert.True(document.Paths.ContainsKey(path), $"no path {path}");

        var operations = document.Paths[path].Operations;

        Assert.NotNull(operations);

        return operations;
    }

    /// <summary>One operation, asserted to be declared on the path and verb given.</summary>
    private static OpenApiOperation Operation(
        OpenApiDocument document, string path, HttpMethod method) {
        var operations = Operations(document, path);

        Assert.True(operations.ContainsKey(method), $"no {method} on {path}");

        return operations[method];
    }

    /// <summary>Every operation the document declares, across all paths.</summary>
    private static IEnumerable<OpenApiOperation> EveryOperation(OpenApiDocument document) =>
        document.Paths.Values.SelectMany(path => path.Operations!.Values);

    /// <summary>The component schemas, asserted to be there.</summary>
    private static IDictionary<string, IOpenApiSchema> Schemas(OpenApiDocument document) {
        Assert.NotNull(document.Components);
        Assert.NotNull(document.Components.Schemas);

        return document.Components.Schemas;
    }

    /// <summary>The parameters an operation declares, asserted to be there.</summary>
    private static IList<IOpenApiParameter> Parameters(OpenApiOperation operation) {
        Assert.NotNull(operation.Parameters);

        return operation.Parameters;
    }

    /// <summary>The string literal the generator wrote, unescaped back to its JSON.</summary>

    [Fact]
    public void TheEmittedDocumentIsValidOpenApi() {
        var document = RoundTrip();

        // [OpenApiInfo] names the document; the class-name fallback is what an application
        // declaring nothing gets.
        Assert.Equal("Orders API", document.Info.Title);
        Assert.Equal("3.5.0", document.Info.Version);
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
        Assert.True(document.Paths.ContainsKey("/orders"), "the collection route is missing");

        var byId = Operations(document, "/orders/{id}");

        Assert.True(byId.ContainsKey(HttpMethod.Get));
        Assert.True(byId.ContainsKey(HttpMethod.Delete));

        var collection = Operations(document, "/orders");

        Assert.True(collection.ContainsKey(HttpMethod.Get));
        Assert.True(collection.ContainsKey(HttpMethod.Post));
    }

    /// <summary>
    /// Each parameter is described where it actually comes from. A path token reported as a query
    /// parameter would generate a client that cannot call the endpoint.
    /// </summary>
    [Fact]
    public void ParametersKeepTheirLocation() {
        var document = RoundTrip();

        var byId = Operation(document, "/orders/{id}", HttpMethod.Get);

        var id = Assert.Single(Parameters(byId));

        Assert.Equal("id", id.Name);
        Assert.Equal(ParameterLocation.Path, id.In);
        Assert.True(id.Required);

        var list = Operation(document, "/orders", HttpMethod.Get);

        var locations = Parameters(list).ToDictionary(p => p.Name!, p => p.In);

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

        var create = Operation(document, "/orders", HttpMethod.Post);

        var request = create.RequestBody!.Content!["application/json"].Schema!;

        // The reader resolves the $ref, so reaching the properties proves the component exists.
        Assert.Equal(JsonSchemaType.Object, request.Type);
        Assert.True(request.Properties!.ContainsKey("sku"));
        Assert.Equal(JsonSchemaType.Integer, request.Properties!["quantity"].Type);
        // Tags is declared List<string>?, and the schema now says so - the type carries "null"
        // beside "array" rather than describing a nullable member as always present.
        Assert.Equal(JsonSchemaType.Array | JsonSchemaType.Null, request.Properties!["tags"].Type);
    }

    /// <summary>A type reached through another is written once and referenced.</summary>
    [Fact]
    public void NestedTypesBecomeTheirOwnComponents() {
        var document = RoundTrip();

        Assert.True(Schemas(document).ContainsKey("Order"));
        Assert.True(Schemas(document).ContainsKey("OrderSummary"));

        var summary = Schemas(document)["OrderSummary"];

        Assert.Equal(JsonSchemaType.Object, summary.Properties!["order"].Type);
    }

    /// <summary>
    /// A handler returning nothing declares a response with no content, rather than a body of an
    /// unnameable type.
    /// </summary>
    [Fact]
    public void AVoidHandlerHasNoResponseBody() {
        var document = RoundTrip();

        var delete = Operation(document, "/orders/{id}", HttpMethod.Delete);

        // Collections are no longer initialised by default, so a response that declares no
        // content has a null map rather than an empty one. Both say the same thing.
        Assert.True(delete.Responses!["200"].Content is null or { Count: 0 });
    }

    /// <summary>All operations in the document, flattened out of the path grouping.</summary>
    private static IReadOnlyList<OpenApiOperation> Operations(OpenApiDocument document) =>
        document.Paths.Values.SelectMany(path => path.Operations!.Values).ToArray();

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
        var list = Operation(RoundTrip(), "/orders", HttpMethod.Get);

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

        Assert.Equal("orderGet", Operation(document, "/orders/{id}", HttpMethod.Get).OperationId);
        Assert.Equal("peopleGet", Operation(document, "/customers/{id}", HttpMethod.Get).OperationId);
    }

    /// <summary>
    /// Every operation carries a tag. The emitter wrote none, and specification-first groups by
    /// <c>Tags.FirstOrDefault()?.Name ?? "Default"</c> - so a round-tripped application collapsed
    /// into a single <c>IDefaultService</c> and lost its controller structure entirely.
    /// </summary>
    [Fact]
    public void EveryOperationCarriesATag() {
        Assert.All(EveryOperation(RoundTrip()), operation => Assert.NotEmpty(operation.Tags!));
    }

    /// <summary>
    /// And the tag set is the controller set. No new grouping construct was introduced for this:
    /// the controller already is the group, and the document simply did not say so.
    /// <c>CustomerController</c> carries <c>[Tag("People")]</c>, which is the override.
    /// </summary>
    [Fact]
    public void TheTagSetIsTheControllerSet() {
        var tags = Operations(RoundTrip())
            .SelectMany(operation => operation.Tags!.Select(tag => tag.Name!))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(new[] { "Order", "People" }, tags);
    }

    /// <summary>
    /// A doc comment is prose a developer has already written about the operation. Carrying it
    /// means the document says what the code says, rather than being a shape with no explanation.
    /// </summary>
    [Fact]
    public void DocCommentsBecomeSummaryAndDescription() {
        var get = Operation(RoundTrip(), "/orders/{id}", HttpMethod.Get);

        Assert.Equal("One order, by its identifier.", get.Summary);
        Assert.Equal(
            "Reads from the replica, so an order created in the last few seconds may not be visible yet.",
            get.Description);
    }

    /// <summary>
    /// <c>[Obsolete]</c> and <c>deprecated</c> are the same statement, so a client generated from
    /// the document warns where the application warns instead of the deprecation stopping at the
    /// assembly boundary.
    /// </summary>
    [Fact]
    public void ObsoleteBecomesDeprecated() {
        var operations = Operations(RoundTrip(), "/orders/{id}");

        Assert.True(operations[HttpMethod.Delete].Deprecated);
        Assert.False(operations[HttpMethod.Get].Deprecated);
    }

    /// <summary>
    /// A constraint and a schema facet are one statement written twice. Without this the document
    /// describes a property as merely "a string" while the server rejects most strings, and a
    /// generated client cannot check anything before sending it.
    /// </summary>
    [Fact]
    public void ValidationConstraintsBecomeSchemaFacets() {
        var order = Schemas(RoundTrip())["Order"];

        Assert.Equal(3, order.Properties!["sku"].MinLength);
        Assert.Equal(12, order.Properties!["sku"].MaxLength);
        Assert.Equal("^[A-Z0-9-]+$", order.Properties!["sku"].Pattern);

        Assert.Equal("1", order.Properties!["quantity"].Minimum);
        Assert.Equal("500", order.Properties!["quantity"].Maximum);

        Assert.Equal(8, order.Properties!["tags"].MaxItems);

        Assert.Equal(
            new[] { "standard", "express" },
            order.Properties!["shipping"].Enum!.Select(value => value!.GetValue<string>()));
    }

    /// <summary>
    /// Where the application is deployed is the one thing in the document that cannot be derived
    /// from the code, so it is declared. Without it a generated client has a set of paths and
    /// nowhere to send them.
    /// </summary>
    [Fact]
    public void DeclaredServersAppear() {
        var servers = RoundTrip().Servers;

        Assert.Equal(
            new[] { "https://api.example.com", "https://staging.example.com" },
            servers!.Select(server => server.Url!));

        Assert.Equal("Production", servers![0].Description);
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
            Schemas(first).Keys.OrderBy(k => k, StringComparer.Ordinal),
            Schemas(second).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// A parameter is typed as the handler declared it.
    /// </summary>
    /// <remarks>
    /// Every one of these was written as <c>{"type":"string"}</c> whatever the C# type, so a
    /// document described <c>Search(long count)</c> as taking a string and a generated client had
    /// no reason to reject a value before sending it. The mapping is by name rather than by symbol,
    /// because no symbol survives to the point the document is written.
    /// </remarks>
    [Fact]
    public void ParametersCarryTheirDeclaredType() {
        var parameters = Parameters(Operation(RoundTrip(), "/orders/search/{count}", HttpMethod.Get))
            .ToDictionary(parameter => parameter.Name!, parameter => parameter.Schema!);

        Assert.Equal((JsonSchemaType.Integer, "int64"), (parameters["count"].Type, parameters["count"].Format));
        Assert.Equal(JsonSchemaType.Boolean, parameters["includeCancelled"].Type);
        Assert.Equal((JsonSchemaType.String, "uuid"), (parameters["tenant"].Type, parameters["tenant"].Format));
        Assert.Equal((JsonSchemaType.String, "date-time"), (parameters["placedAfter"].Type, parameters["placedAfter"].Format));
        Assert.Equal(JsonSchemaType.Number, parameters["minimumTotal"].Type);
        Assert.Equal((JsonSchemaType.Number, "double"), (parameters["weighting"].Type, parameters["weighting"].Format));

        Assert.Equal((JsonSchemaType.Integer, "int32"), (parameters["X-Page"].Type, parameters["X-Page"].Format));
    }

    /// <summary>
    /// A path template is a parameter name and nothing else - no constraint, no catch-all marker.
    /// </summary>
    /// <remarks>
    /// Both are routing syntax a document cannot express. The marker says how much of the path a
    /// token takes, which costs something worth knowing: a specification round-tripped back through
    /// the build task gives a single-segment token where the source route had a catch-all. Better
    /// than emitting a template no OpenAPI reader accepts.
    /// </remarks>
    [Fact]
    public void APathTemplateCarriesNoRoutingSyntax() {
        var document = RoundTrip();

        Assert.True(document.Paths.ContainsKey("/orders/search/{count}"));
        Assert.True(document.Paths.ContainsKey("/orders/receipts/{path}"));
        Assert.DoesNotContain(document.Paths.Keys, path => path.Contains(':') || path.Contains('*'));
    }

    /// <summary>The remaining scalar shapes a value parsed from text can be declared as.</summary>
    [Fact]
    public void TheRestOfTheScalarShapesMapToo() {
        var parameters = Parameters(Operation(RoundTrip(), "/orders/on/{day}", HttpMethod.Get))
            .ToDictionary(parameter => parameter.Name!, parameter => parameter.Schema!);

        Assert.Equal((JsonSchemaType.String, "date"), (parameters["day"].Type, parameters["day"].Format));
        Assert.Equal((JsonSchemaType.String, "uri"), (parameters["callback"].Type, parameters["callback"].Format));
        Assert.Equal((JsonSchemaType.Number, "float"), (parameters["weight"].Type, parameters["weight"].Format));
        Assert.Equal((JsonSchemaType.Integer, "int32"), (parameters["batch"].Type, parameters["batch"].Format));
    }

    /// <summary>The document declares the groups its operations reference.</summary>
    [Fact]
    public void TheDocumentDeclaresItsTags() {
        var document = RoundTrip();

        var declared = document.Tags!.Select(tag => tag.Name!).ToList();

        Assert.Contains("Order", declared);
        Assert.Contains("People", declared);

        var used = document.Paths.Values
            .SelectMany(path => path.Operations!.Values)
            .SelectMany(operation => operation.Tags!)
            .Select(tag => tag.Name!)
            .Distinct();

        Assert.All(used, tag => Assert.Contains(tag, declared));
    }

    /// <summary>
    /// A parameter the binder answers with a default is not required, and an enum parameter
    /// carries the vocabulary the wire converters are generated from.
    /// </summary>
    [Fact]
    public void DefaultedAndEnumParametersKeepTheirFacts() {
        var list = Operation(RoundTrip(), "/orders", HttpMethod.Get);
        var parameters = Parameters(list).ToDictionary(p => p.Name!);

        Assert.False(parameters["limit"].Required);
        Assert.Equal(JsonSchemaType.Integer, parameters["limit"].Schema!.Type);

        var priority = parameters["priority"].Schema!;

        Assert.Equal(
            new[] { "standard", "express", "nextDay" },
            priority.Enum!.Select(value => value.GetValue<string>()));
    }

}
