using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests;

/// <summary>
/// The generated OpenAPI document, driven through the copy of the generator that lives in
/// <c>Hardened.SourceGenerator</c>.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than only beside the wrapper's tests for the same reason
/// <see cref="OpenApiVersionTests"/> gives: this assembly ships as source and is compiled into
/// several generator projects, so a test in one wrapper covers that wrapper's copy and nothing
/// else. Emission became opt-in, and every fixture in this suite that used to reach the document
/// generator incidentally stopped reaching it - which is a whole file going dark behind a suite
/// that still passes.
/// </para>
/// <para>
/// So these opt in explicitly, and walk enough handler shapes to cover writing the document rather
/// than only deciding to write one.
/// </para>
/// </remarks>
public class OpenApiDocumentEmissionTests {

    /// <summary>
    /// What an application carries for a document to be emitted at all.
    /// </summary>
    private const string Enable =
        "[Hardened.Shared.Runtime.Attributes.Enable<" +
        "Hardened.Web.Runtime.OpenApi.OpenApiDocumentPublishing>]";

    private static string Application(string controllers, string moduleAttributes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        {{moduleAttributes}}
        public partial class Application { }

        {{controllers}}
        """;

    /// <summary>
    /// One controller per response and parameter shape the document has to describe, so the writers
    /// under OpenApiDocument/ are walked rather than only entered.
    /// </summary>
    private const string Controllers = """
        public record Order(string Id, int Quantity, decimal Total);

        public record CreateOrder(string Sku, int Quantity);

        public class OrderController {

            /// <summary>Every order.</summary>
            /// <remarks>Described so the summary and description both reach the document.</remarks>
            [Get("/orders")]
            public IEnumerable<Order> All() => Array.Empty<Order>();

            /// <summary>One order.</summary>
            [Get("/orders/{id}")]
            public Order? Get(string id) => null;

            [Get("/orders/search")]
            public IEnumerable<Order> Search(
                [FromQueryString] string sku, [FromQueryString] int? limit) =>
                Array.Empty<Order>();

            [Post("/orders")]
            public Task<Order> Create([FromBody] CreateOrder order) =>
                Task.FromResult(new Order("1", order.Quantity, 0m));

            [Put("/orders/{id}")]
            public Task<Order> Replace(string id, [FromBody] CreateOrder order) =>
                Task.FromResult(new Order(id, order.Quantity, 0m));

            [Delete("/orders/{id}")]
            public Task Remove(string id) => Task.CompletedTask;

            [Get("/orders/stream")]
            public async IAsyncEnumerable<Order> Stream() {
                await Task.CompletedTask;
                yield break;
            }
        }
        """;

    private static string Generate(string moduleAttributes) =>
        RequestGeneratorHarness
            .Generate(Application(Controllers, moduleAttributes))
            .AssertNoErrors()
            .SourceContaining("OpenApiDocument");

    /// <summary>The JSON the generated source carries, inflated back out of the byte array.</summary>
    private static string Extract(string generatedSource) {
        var match = Regex.Match(
            generatedSource, @"new byte\[\]\s*\{(.*?)\}\s*;", RegexOptions.Singleline);

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(byte.Parse)
            .ToArray();

        using var source = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return Encoding.UTF8.GetString(inflated.ToArray());
    }

    /// <summary>
    /// No marker, no document - the point of making emission opt-in rather than only the route.
    /// </summary>
    [Fact]
    public void NoDocumentIsEmittedWithoutTheMarker() {
        var result = RequestGeneratorHarness
            .Generate(Application(Controllers))
            .AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("OpenApiDocument"));
    }

    [Fact]
    public void TheDocumentIsEmittedWithTheMarker() {
        Assert.Contains("OpenApiDocument", Generate(Enable));
        Assert.Contains("GZip", Generate(Enable));
    }

    /// <summary>
    /// Compressed bytes over a metadata blob, not a string literal in the <c>#US</c> heap.
    /// </summary>
    [Fact]
    public void TheDocumentIsCarriedAsGZippedBytes() {
        var source = Generate(Enable);

        Assert.Contains("ReadOnlySpan<byte>", source);
        Assert.Contains("new byte[]", source);
        Assert.DoesNotContain("\"openapi\":", source);
    }

    /// <summary>
    /// And it inflates back to the document describing the handlers the application declared.
    /// </summary>
    [Fact]
    public void TheInflatedDocumentDescribesEveryRoute() {
        using var document = JsonDocument.Parse(Extract(Generate(Enable)));

        var root = document.RootElement;

        Assert.True(root.TryGetProperty("openapi", out _));

        var paths = root.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/orders", out var orders));
        Assert.True(orders.TryGetProperty("get", out _));
        Assert.True(orders.TryGetProperty("post", out _));

        Assert.True(paths.TryGetProperty("/orders/{id}", out var byId));
        Assert.True(byId.TryGetProperty("get", out _));
        Assert.True(byId.TryGetProperty("put", out _));
        Assert.True(byId.TryGetProperty("delete", out _));

        Assert.True(paths.TryGetProperty("/orders/search", out _));
        Assert.True(paths.TryGetProperty("/orders/stream", out _));
    }

    /// <summary>
    /// The schema of a returned type reaches the document, not just its name.
    /// </summary>
    [Fact]
    public void ReturnedTypesBecomeSchemas() {
        using var document = JsonDocument.Parse(Extract(Generate(Enable)));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("Order", out var order));
        Assert.True(schemas.TryGetProperty("CreateOrder", out _));

        var properties = order.GetProperty("properties");

        Assert.True(properties.TryGetProperty("id", out _));
        Assert.True(properties.TryGetProperty("quantity", out _));
        Assert.True(properties.TryGetProperty("total", out _));
    }

    /// <summary>
    /// A route token is a path parameter and a query value is a query parameter, and the document
    /// says which is which.
    /// </summary>
    [Fact]
    public void ParametersAreDescribedWithTheirSource() {
        using var document = JsonDocument.Parse(Extract(Generate(Enable)));

        var parameters = document.RootElement
            .GetProperty("paths").GetProperty("/orders/{id}").GetProperty("get")
            .GetProperty("parameters");

        var id = parameters.EnumerateArray().Single();

        Assert.Equal("id", id.GetProperty("name").GetString());
        Assert.Equal("path", id.GetProperty("in").GetString());

        var query = document.RootElement
            .GetProperty("paths").GetProperty("/orders/search").GetProperty("get")
            .GetProperty("parameters");

        Assert.Contains(
            query.EnumerateArray(),
            parameter => parameter.GetProperty("in").GetString() == "query"
                         && parameter.GetProperty("name").GetString() == "sku");
    }

    /// <summary>
    /// A doc comment on a handler is the operation's summary, which is the only reason to read the
    /// XML in the first place.
    /// </summary>
    [Fact]
    public void DocCommentsBecomeSummaries() {
        var document = Extract(Generate(Enable));

        Assert.Contains("Every order.", document);
        Assert.Contains("One order.", document);
    }

    /// <summary>
    /// The same input compresses to the same bytes.
    /// </summary>
    /// <remarks>
    /// Incremental generation and reproducible builds both require it, and a compressor stamping a
    /// timestamp into its header would break both silently - still a valid document, just a
    /// different one on every run. <c>GZipStream</c> writes MTIME as zero, which is what makes this
    /// hold; asserted so it stays true rather than stays assumed.
    /// </remarks>
    [Fact]
    public void TheEmittedBytesAreTheSameOnEveryRun() {
        Assert.Equal(Generate(Enable), Generate(Enable));
    }

    /// <summary>
    /// The provider registration is emitted with the document, so no application writes an
    /// <c>AddSingleton</c> by hand.
    /// </summary>
    [Fact]
    public void TheProviderIsRegisteredAtTheDeclaredPath() {
        var routing = RequestGeneratorHarness
            .Generate(Application(Controllers, Enable))
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("OpenApiDocumentProvider", routing);
        Assert.Contains("Application.OpenApiDocument.GZip", routing);
        Assert.Contains("\"/openapi.json\"", routing);
    }

    [Fact]
    public void NoProviderIsRegisteredWithoutTheMarker() {
        var routing = RequestGeneratorHarness
            .Generate(Application(Controllers))
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.DoesNotContain("OpenApiDocumentProvider", routing);
    }

    /// <summary>
    /// The generator reads the facet rather than the marker's name, so an application serving the
    /// document somewhere else declares its own marker and needs no generator change.
    /// </summary>
    [Fact]
    public void AnApplicationsOwnMarkerChoosesThePath() {
        const string marker = """
            [Hardened.Web.Runtime.OpenApi.OpenApiDocumentPath("/spec.json")]
            public sealed class SpecEndpoint { }
            """;

        var result = RequestGeneratorHarness
            .Generate(Application(marker + Controllers, "[Enable<SpecEndpoint>]"))
            .AssertNoErrors();

        Assert.Contains("\"/spec.json\"", result.SourceContaining("Routing"));
        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("OpenApiDocument"));
    }

    #region what the document says about code-first parameters and members

    /// <summary>The application the fidelity tests drive: enums, defaults, constraints, nulls.</summary>
    /// <remarks>
    /// <c>Priority</c> deliberately appears in no body. <c>Carrier</c> rides in
    /// <c>Shipment</c> too, so the response walk collected its vocabulary and the parameter test
    /// passed while parameter-only enums were broken - the second trial's exact shape.
    /// </remarks>
    private const string FidelityControllers = """
        public enum Carrier { Dhl, Fedex, RoyalMail }

        public enum Priority { Low, High }

        public record Shipment(string Id, int Quantity, string? Note, Carrier Carrier);

        public record NewShipment(
            [property: ValidationModules.Constraints.Range("0.5", "30", ExclusiveMin = true)]
            decimal WeightKg,
            [property: ValidationModules.Constraints.ItemCount(Min = 1, Max = 10)]
            List<Shipment> Batch);

        public record Eta(
            [property: System.Text.Json.Serialization.JsonPropertyName("mins")] int Minutes,
            string Kind = "standard",
            Carrier Carrier = Carrier.Dhl,
            int? Retries = null,
            bool Express = false,
            double Factor = 1.5,
            char Zone = 'A',
            DayOfWeek Day = DayOfWeek.Monday);

        public record Page<T>(List<T> Items, int Total);

        public record Courier(string Name);

        public class ShipmentController {
            [Get("/shipments")]
            public Task<List<Shipment>> List(
                [FromQueryString] int limit = 20, [FromQueryString] Carrier? carrier = null) =>
                Task.FromResult(new List<Shipment>());

            [Get("/shipments/urgent")]
            public Task<List<Shipment>> Urgent([FromQueryString] Priority priority) =>
                Task.FromResult(new List<Shipment>());

            [Get("/shipments/paged")]
            public Task<List<Shipment>> Paged(
                [FromQueryString] long? cursor = null, [FromQueryString] int? limit = null) =>
                Task.FromResult(new List<Shipment>());

            [Post("/shipments")]
            public Task<Shipment> Create([FromBody] NewShipment body) =>
                Task.FromResult(new Shipment("1", 1, null, Carrier.Dhl));

            [Post("/shipments/{id}/archive")]
            public Task Archive(string id) => Task.CompletedTask;

            [Get("/shipments/{id}/eta")]
            public Task<Eta> Eta(string id) => Task.FromResult(new Eta(7));

            [Get("/shipments/pages")]
            public Task<Page<Shipment>> ShipmentPage() =>
                Task.FromResult(new Page<Shipment>(new List<Shipment>(), 0));

            [Get("/couriers/pages")]
            public Task<Page<Courier>> CourierPage() =>
                Task.FromResult(new Page<Courier>(new List<Courier>(), 0));

            [Get("/couriers/batches")]
            public Task<Page<Courier[]>> CourierBatches() =>
                Task.FromResult(new Page<Courier[]>(new List<Courier[]>(), 0));

            [Get("/couriers/counts")]
            public Task<Page<int?>> CourierCounts() =>
                Task.FromResult(new Page<int?>(new List<int?>(), 0));
        }
        """;

    private static JsonElement FidelityDocument() =>
        JsonDocument.Parse(Extract(
            RequestGeneratorHarness
                .Generate(Application(FidelityControllers, Enable))
                .AssertNoErrors()
                .SourceContaining("OpenApiDocument"))).RootElement;

    private static JsonElement ListParameter(JsonElement document, string name) {
        foreach (var parameter in document
                     .GetProperty("paths").GetProperty("/shipments").GetProperty("get")
                     .GetProperty("parameters").EnumerateArray()) {
            if (parameter.GetProperty("name").GetString() == name) {
                return parameter;
            }
        }

        throw new Xunit.Sdk.XunitException($"No parameter named '{name}'.");
    }

    /// <summary>
    /// A parameter the binder answers with a default is one the caller may omit, and the document
    /// now says so. It said required: true.
    /// </summary>
    [Fact]
    public void AParameterWithADefaultIsNotRequired() {
        Assert.False(ListParameter(FidelityDocument(), "limit").GetProperty("required").GetBoolean());
    }

    /// <summary>
    /// An enum parameter carries the same vocabulary the wire converters are generated from.
    /// It carried {"type":"string"} and nothing else.
    /// </summary>
    [Fact]
    public void AnEnumParameterCarriesItsVocabulary() {
        var document = FidelityDocument();
        var schema = ListParameter(document, "carrier").GetProperty("schema");

        // One component per enum, referenced from the parameter as from a member, so a client
        // generator produces one type per server enum rather than one per use.
        Assert.Equal("#/components/schemas/Carrier", schema.GetProperty("$ref").GetString());

        var carrier = document.GetProperty("components").GetProperty("schemas").GetProperty("Carrier");

        Assert.Equal("string", carrier.GetProperty("type").GetString());
        Assert.Equal(
            new[] { "dhl", "fedex", "royalMail" },
            carrier.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// The unmasked half of the assertion above. Carrier also rides in the response body, so the
    /// body walk collected its vocabulary and the test passed while the parameter walk collected
    /// nothing. Priority appears in no body: its vocabulary exists only because the parameter
    /// transform now captures it.
    /// </summary>
    [Fact]
    public void AParameterOnlyEnumCarriesItsVocabulary() {
        var document = FidelityDocument();
        var schema = Parameter(document, "/shipments/urgent", "priority").GetProperty("schema");

        Assert.Equal("#/components/schemas/Priority", schema.GetProperty("$ref").GetString());

        var priority = document.GetProperty("components").GetProperty("schemas").GetProperty("Priority");

        Assert.Equal("string", priority.GetProperty("type").GetString());
        Assert.Equal(
            new[] { "low", "high" },
            priority.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// int? and long? describe as the integers they are. The unwrap hands on a definition named
    /// with the C# keyword, and the schema switch matched only the CLR names a bare parameter
    /// produces - so a nullable scalar published as a string while its bare twin published as an
    /// integer.
    /// </summary>
    [Fact]
    public void ANullableScalarParameterIsAnInteger() {
        var document = FidelityDocument();

        var limit = Parameter(document, "/shipments/paged", "limit").GetProperty("schema");

        Assert.Equal("integer", limit.GetProperty("type").GetString());
        Assert.Equal("int32", limit.GetProperty("format").GetString());

        var cursor = Parameter(document, "/shipments/paged", "cursor").GetProperty("schema");

        Assert.Equal("integer", cursor.GetProperty("type").GetString());
        Assert.Equal("int64", cursor.GetProperty("format").GetString());
    }

    /// <summary>
    /// A bare Task is void with a different spelling. The schema writer used to walk Task itself,
    /// so the 200 carried a Task schema and components gained its BCL entourage.
    /// </summary>
    [Fact]
    public void ABareTaskPublishesNoSchemaAtAll() {
        var document = FidelityDocument();

        var ok = document.GetProperty("paths").GetProperty("/shipments/{id}/archive")
            .GetProperty("post").GetProperty("responses").GetProperty("200");

        Assert.False(ok.TryGetProperty("content", out _));

        if (document.TryGetProperty("components", out var components)) {
            foreach (var schema in components.GetProperty("schemas").EnumerateObject()) {
                Assert.NotEqual("Task", schema.Name);
            }
        }
    }

    /// <summary>
    /// [ItemCount] on an array of referenced objects keeps its bounds. The guard against writing
    /// facets beside a $ref searched the whole schema string, so an items reference cost the
    /// array every facet it had - and the bounds sit on the array, where no reference is.
    /// </summary>
    [Fact]
    public void ItemCountOnAnArrayOfReferencesKeepsItsBounds() {
        var batch = FidelityDocument().GetProperty("components").GetProperty("schemas")
            .GetProperty("NewShipment").GetProperty("properties").GetProperty("batch");

        Assert.Equal(1, batch.GetProperty("minItems").GetInt32());
        Assert.Equal(10, batch.GetProperty("maxItems").GetInt32());
        Assert.Equal(
            "#/components/schemas/Shipment",
            batch.GetProperty("items").GetProperty("$ref").GetString());
    }

    private static JsonElement Parameter(JsonElement document, string path, string name) {
        foreach (var parameter in document
                     .GetProperty("paths").GetProperty(path).GetProperty("get")
                     .GetProperty("parameters").EnumerateArray()) {
            if (parameter.GetProperty("name").GetString() == name) {
                return parameter;
            }
        }

        throw new Xunit.Sdk.XunitException($"No parameter named '{name}' at '{path}'.");
    }

    /// <summary>
    /// String-spelled Range bounds are numbers in the document, and an exclusive flag is the
    /// 2020-12 spelling. They were "minimum": "0.5" and a boolean in a 3.2 document.
    /// </summary>
    [Fact]
    public void StringSpelledBoundsArePublishedAsNumbers() {
        var weight = FidelityDocument()
            .GetProperty("components").GetProperty("schemas").GetProperty("NewShipment")
            .GetProperty("properties").GetProperty("weightKg");

        Assert.Equal(0.5m, weight.GetProperty("exclusiveMinimum").GetDecimal());
        Assert.Equal(30m, weight.GetProperty("maximum").GetDecimal());
        Assert.False(weight.TryGetProperty("minimum", out _));
    }

    /// <summary>
    /// A nullable member says so, and a non-nullable value member is required. The service sent
    /// null for members the document typed non-nullable, and always sent members it left optional.
    /// </summary>
    [Fact]
    public void NullabilityReachesTheSchema() {
        var shipment = FidelityDocument()
            .GetProperty("components").GetProperty("schemas").GetProperty("Shipment");

        var note = shipment.GetProperty("properties").GetProperty("note").GetProperty("type");

        Assert.Equal(JsonValueKind.Array, note.ValueKind);
        Assert.Equal(
            new[] { "string", "null" },
            note.EnumerateArray().Select(value => value.GetString()));

        var required = shipment.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).ToList();

        Assert.Contains("id", required);
        Assert.Contains("quantity", required);
        Assert.Contains("carrier", required);
        Assert.DoesNotContain("note", required);
    }

    private static JsonElement Schema(string name) =>
        FidelityDocument().GetProperty("components").GetProperty("schemas").GetProperty(name);

    private static List<string?> Required(JsonElement schema) =>
        schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToList();

    /// <summary>
    /// A member renamed for the wire with [JsonPropertyName] is published under that name. It was
    /// published under the camelCased member name, which the wire never carries, so a client
    /// generated from the document read nothing for it and nothing failed.
    /// </summary>
    [Fact]
    public void AJsonPropertyNameIsTheDocumentsName() {
        var eta = Schema("Eta");
        var properties = eta.GetProperty("properties");

        Assert.True(properties.TryGetProperty("mins", out _));
        Assert.False(properties.TryGetProperty("minutes", out _));
        Assert.Contains("mins", Required(eta));
    }

    /// <summary>
    /// A positional parameter with a default is a member the caller may omit: it is not required,
    /// and the default is written in the wire's vocabulary. Every non-nullable member was required.
    /// </summary>
    [Fact]
    public void APositionalDefaultIsPublishedAndTheMemberIsNotRequired() {
        var eta = Schema("Eta");
        var properties = eta.GetProperty("properties");

        Assert.Equal("standard", properties.GetProperty("kind").GetProperty("default").GetString());
        Assert.Equal("dhl", properties.GetProperty("carrier").GetProperty("default").GetString());
        Assert.False(properties.GetProperty("express").GetProperty("default").GetBoolean());
        Assert.Equal(1.5, properties.GetProperty("factor").GetProperty("default").GetDouble());
        Assert.Equal("A", properties.GetProperty("zone").GetProperty("default").GetString());
        Assert.Equal("Monday", properties.GetProperty("day").GetProperty("default").GetString());
        Assert.False(properties.GetProperty("retries").TryGetProperty("default", out _));
        Assert.Equal(new[] { "mins" }, Required(eta));
    }

    /// <summary>
    /// A property initializer says what a positional default says - this when nothing sends it - so
    /// the member is not required either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mutable-DTO spelling, and it was published as required because an initializer is syntax
    /// rather than a symbol and nothing looked for it. This repository's own Invoke fixture is that
    /// shape: four settable properties, three of them carrying <c>= ""</c> or <c>= []</c>, all
    /// documented as demands on a caller the handler was filling in for.
    /// </para>
    /// <para>
    /// No <c>default</c> is written. The initializer is an expression rather than a constant, and
    /// half of them - <c>= []</c>, <c>= new()</c>, <c>= DateTime.UtcNow</c> - have no JSON spelling
    /// at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void APropertyInitializerIsADefaultAndTheMemberIsNotRequired() {
        var document = JsonDocument.Parse(Extract(
            RequestGeneratorHarness
                .Generate(Application(
                    """
                    public class Manifest {
                        public string Id { get; set; } = "";

                        public List<string> Records { get; set; } = [];

                        public string Carrier { get; set; }

                        public int Quantity { get; set; }
                    }

                    public class ManifestController {
                        [Post("/manifests")]
                        public Manifest Place(Manifest manifest) => manifest;
                    }
                    """,
                    Enable))
                .AssertNoErrors()
                .SourceContaining("OpenApiDocument"))).RootElement;

        var manifest = document
            .GetProperty("components").GetProperty("schemas").GetProperty("Manifest");

        Assert.Equal(new[] { "carrier", "quantity" }, Required(manifest));
        Assert.False(manifest.GetProperty("properties").GetProperty("id")
            .TryGetProperty("default", out _));
    }

    /// <summary>
    /// A constructed type is one component per set of arguments. Page&lt;Shipment&gt; and
    /// Page&lt;Courier&gt; shared one component named Page, written from whichever was reached
    /// first, so the second operation was documented as returning the first one's items.
    /// </summary>
    [Fact]
    public void AConstructedTypeIsNamedByItsArguments() {
        var schemas = FidelityDocument().GetProperty("components").GetProperty("schemas");

        Assert.False(schemas.TryGetProperty("Page", out _));

        var shipments = schemas.GetProperty("PageOfShipment").GetProperty("properties")
            .GetProperty("items").GetProperty("items").GetProperty("$ref").GetString();
        var couriers = schemas.GetProperty("PageOfCourier").GetProperty("properties")
            .GetProperty("items").GetProperty("items").GetProperty("$ref").GetString();

        Assert.Equal("#/components/schemas/Shipment", shipments);
        Assert.Equal("#/components/schemas/Courier", couriers);
        Assert.True(schemas.TryGetProperty("PageOfCourierArray", out _));
        Assert.True(schemas.TryGetProperty("PageOfInt32", out _));
    }

    /// <summary>
    /// [OpenApiInfo] names the document; without it the entry point's class name and "1.0.0"
    /// stand in, because they are the only facts the generator has.
    /// </summary>
    [Fact]
    public void OpenApiInfoNamesTheDocument() {
        var document = JsonDocument.Parse(Extract(
            RequestGeneratorHarness
                .Generate(Application(
                    FidelityControllers,
                    Enable + "\n[Hardened.Web.Runtime.Attributes.OpenApiInfo(\"Shipments API\", \"3.1.4\")]"))
                .AssertNoErrors()
                .SourceContaining("OpenApiDocument"))).RootElement;

        var info = document.GetProperty("info");

        Assert.Equal("Shipments API", info.GetProperty("title").GetString());
        Assert.Equal("3.1.4", info.GetProperty("version").GetString());
    }

    #endregion

    #region what [Authorize<TAuth>] declares for the document

    /// <summary>
    /// Schemes as types, used where they are enforced. Using one is declaring it: the writer
    /// collects every TAuth the handlers name into components.securitySchemes.
    /// </summary>
    private const string SecuredControllers = """
        [Hardened.Requests.Abstract.Authorization.HttpAuthenticationScheme("bearer", BearerFormat = "JWT")]
        public sealed class BearerAuth : Hardened.Requests.Abstract.Authorization.IAuthenticationScheme;

        [Hardened.Requests.Abstract.Authorization.OAuth2AuthenticationScheme(
            Hardened.Requests.Abstract.Authorization.OAuth2Flow.ClientCredentials, TokenUrl = "https://id.example/token")]
        public sealed class PetsOAuth : Hardened.Requests.Abstract.Authorization.IAuthenticationScheme;

        public record Pet(string Id);

        public class PetController {
            [Get("/pets/{id}")]
            [Hardened.Requests.Runtime.Authorization.Authorize<BearerAuth>]
            public Task<Pet> Get(string id) => Task.FromResult(new Pet(id));

            [Post("/pets")]
            [Hardened.Requests.Runtime.Authorization.Authorize<PetsOAuth>]
            [Hardened.Requests.Runtime.Authorization.AuthorizeGrants("pets:write", "pets:admin")]
            public Task<Pet> Create() => Task.FromResult(new Pet("1"));

            [Delete("/pets/{id}")]
            [Hardened.Requests.Runtime.Authorization.Authorize<BearerAuth>]
            [Hardened.Requests.Runtime.Authorization.AuthorizeGrants("pets:admin")]
            public Task Remove(string id) => Task.CompletedTask;

            [Get("/pets")]
            public Task<List<Pet>> List() => Task.FromResult(new List<Pet>());
        }
        """;

    private static JsonElement SecuredDocument() =>
        JsonDocument.Parse(Extract(
            RequestGeneratorHarness
                .Generate(Application(SecuredControllers, Enable))
                .AssertNoErrors()
                .SourceContaining("OpenApiDocument"))).RootElement;

    /// <summary>
    /// Every scheme the handlers name, keyed by its type's name, shaped by its type's attribute.
    /// Code-first published no securitySchemes at all before this.
    /// </summary>
    [Fact]
    public void UsedSchemesAreDeclaredInComponents() {
        var schemes = SecuredDocument().GetProperty("components").GetProperty("securitySchemes");

        var bearer = schemes.GetProperty("BearerAuth");

        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var oauth = schemes.GetProperty("PetsOAuth");

        Assert.Equal("oauth2", oauth.GetProperty("type").GetString());
        Assert.Equal(
            "https://id.example/token",
            oauth.GetProperty("flows").GetProperty("clientCredentials")
                .GetProperty("tokenUrl").GetString());
    }

    /// <summary>
    /// Grants become the requirement's scopes only where the scheme kind can carry them - OAuth2 -
    /// mirroring the rule the OpenAPI reader applies in the other direction. On an http scheme the
    /// operation still says "authenticated via this scheme", and the enforcement of the grants is
    /// unchanged either way.
    /// </summary>
    [Fact]
    public void GrantsBecomeScopesOnlyWhereTheSchemeCarriesThem() {
        var paths = SecuredDocument().GetProperty("paths");

        var create = paths.GetProperty("/pets").GetProperty("post");
        var oauthRequirement = Assert.Single(create.GetProperty("security").EnumerateArray());

        Assert.Equal(
            new[] { "pets:write", "pets:admin" },
            oauthRequirement.GetProperty("PetsOAuth").EnumerateArray()
                .Select(scope => scope.GetString()));

        var remove = paths.GetProperty("/pets/{id}").GetProperty("delete");
        var bearerRequirement = Assert.Single(remove.GetProperty("security").EnumerateArray());

        Assert.Equal(0, bearerRequirement.GetProperty("BearerAuth").GetArrayLength());
    }

    /// <summary>An operation naming no scheme declares no security, exactly as before.</summary>
    [Fact]
    public void AnUnsecuredOperationDeclaresNothing() {
        var list = SecuredDocument().GetProperty("paths").GetProperty("/pets").GetProperty("get");

        Assert.False(list.TryGetProperty("security", out _));
    }

    /// <summary>
    /// The status enforcing a requirement produces. AuthorizationFilter refuses an
    /// unauthenticated caller before the handler runs, with a WWW-Authenticate challenge and the
    /// standard error body - the document published the requirement and promised nothing about
    /// the refusal.
    /// </summary>
    [Fact]
    public void ASecuredOperationPublishesTheFourOhOne() {
        var document = SecuredDocument();

        var responses = document.GetProperty("paths").GetProperty("/pets/{id}")
            .GetProperty("get").GetProperty("responses");

        var unauthorized = responses.GetProperty("401");

        Assert.True(unauthorized.GetProperty("headers").TryGetProperty("WWW-Authenticate", out _));
        Assert.Equal(
            "#/components/schemas/ErrorModel",
            unauthorized.GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString());

        Assert.True(document.GetProperty("components").GetProperty("schemas")
            .TryGetProperty("ErrorModel", out _));
    }

    /// <summary>And an operation naming no scheme answers no 401, so nothing is added to it.</summary>
    [Fact]
    public void AnUnsecuredOperationPublishesNoFourOhOne() {
        var responses = SecuredDocument().GetProperty("paths").GetProperty("/pets")
            .GetProperty("get").GetProperty("responses");

        Assert.False(responses.TryGetProperty("401", out _));
    }

    /// <summary>
    /// A scheme-shape attribute on the controller itself is a silent no-op - it is read from the
    /// type [Authorize&lt;TScheme&gt;] names and from nowhere else. The second trial's code-first
    /// arm put it exactly here, published nothing, and concluded the emission did not exist.
    /// </summary>
    [Fact]
    public void ASchemeAttributeOnTheControllerIsReported() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public record Pet(string Id);

            [Hardened.Requests.Abstract.Authorization.HttpAuthenticationScheme("bearer")]
            public class PetController {
                [Get("/pets/{id}")]
                public Task<Pet> Get(string id) => Task.FromResult(new Pet(id));
            }
            """, Enable));

        var diagnostic = Assert.Single(
            result.GeneratorDiagnostics,
            entry => entry.Id == Hardened.SourceGenerator.Requests
                .SecuritySchemeDiagnostics.MisplacedSchemeId);

        Assert.Contains("PetController", diagnostic.GetMessage());
        Assert.Contains("Authorize<TScheme>", diagnostic.GetMessage());
    }

    #endregion

    #region shapes the document used to get wrong

    /// <summary>
    /// Every shape in this region, in one application, so the document is read once.
    /// </summary>
    private const string ShapeControllers = """
        public enum Priority { Low, InProgress }

        public sealed class Address { public string? City { get; set; } }

        public class Registration {
            public string? Name { get; set; }
            public Address? Home { get; set; }
        }

        public class ShapeController {
            [Get("/download")]
            [Produces("application/octet-stream")]
            public byte[] Download() => new byte[] { 1, 2, 3 };

            [Delete("/item", SuccessStatus = 204)]
            public string Remove() => "this body is not written";

            [Post("/registrations")]
            public string Register(Registration registration) => registration.Name ?? "";

            [Get("/files/{*path}")]
            public string File(string path) => path;

            [Get("/item/{id}")]
            public string Item(string id) => id;

            [Get("/counts")]
            public Dictionary<Priority, int> Counts() => new();
        }
        """;

    private static JsonElement ShapeDocument() =>
        JsonDocument.Parse(Extract(
            RequestGeneratorHarness
                .Generate(Application(ShapeControllers, Enable))
                .AssertNoErrors()
                .SourceContaining("OpenApiDocument"))).RootElement;

    /// <summary>
    /// A byte array is the payload, not a list of numbers.
    /// </summary>
    /// <remarks>
    /// Every element type resolves through the primitive map and a byte maps to an int32, so the
    /// array branch described an octet-stream body as a JSON array of integers. A generated client
    /// built from that reads numbers off a body that is not JSON.
    /// </remarks>
    [Fact]
    public void AByteArrayIsABinaryPayload() {
        var schema = ShapeDocument()
            .GetProperty("paths").GetProperty("/download").GetProperty("get")
            .GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/octet-stream")
            .GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal("binary", schema.GetProperty("format").GetString());
    }

    /// <summary>
    /// A 204 describes no body, because there is not one to describe.
    /// </summary>
    /// <remarks>
    /// The handler returns a string and declares 204, which means both things it says: the writer
    /// does not send the value. The document said the response carried a JSON string, so a strict
    /// client waited to read one off an empty body.
    /// </remarks>
    [Fact]
    public void ADeclared204CarriesNoContent() {
        var response = ShapeDocument()
            .GetProperty("paths").GetProperty("/item").GetProperty("delete")
            .GetProperty("responses").GetProperty("204");

        Assert.False(response.TryGetProperty("content", out _));
    }

    /// <summary>
    /// A nullable member pointing at a component says so, the way a nullable scalar does.
    /// </summary>
    /// <remarks>
    /// A reference carries no type of its own, so the null goes beside it under <c>anyOf</c>. The
    /// nullability was dropped entirely, which put two spellings of one annotation in one schema:
    /// <c>["string","null"]</c> on a scalar and silence on a reference.
    /// </remarks>
    [Fact]
    public void ANullableReferenceMemberDeclaresItsNull() {
        var home = ShapeDocument()
            .GetProperty("components").GetProperty("schemas").GetProperty("Registration")
            .GetProperty("properties").GetProperty("home");

        var branches = home.GetProperty("anyOf").EnumerateArray().ToList();

        Assert.Equal(
            "#/components/schemas/Address", branches[0].GetProperty("$ref").GetString());
        Assert.Equal("null", branches[1].GetProperty("type").GetString());
    }

    /// <summary>
    /// A catch-all token says it is one, since the template cannot.
    /// </summary>
    /// <remarks>
    /// <c>{*path}</c> and <c>{path}</c> reduce to the same expression - a template expression is a
    /// name and nothing else - so a reader given the template alone escapes the separators in the
    /// value it sends and gets a 404 from a route built to accept them.
    /// </remarks>
    [Fact]
    public void ACatchAllTokenIsMarked() {
        var parameter = ShapeDocument()
            .GetProperty("paths").GetProperty("/files/{path}").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Single();

        Assert.Equal("path", parameter.GetProperty("name").GetString());
        Assert.True(parameter.GetProperty("x-hardened-catch-all").GetBoolean());
    }

    /// <summary>A single-segment token says nothing, which is the common case.</summary>
    [Fact]
    public void AnOrdinaryPathTokenIsNotMarked() {
        var parameter = ShapeDocument()
            .GetProperty("paths").GetProperty("/item/{id}").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Single();

        Assert.Equal("id", parameter.GetProperty("name").GetString());
        Assert.False(parameter.TryGetProperty("x-hardened-catch-all", out _));
    }

    /// <summary>
    /// An enum key names the vocabulary the map accepts.
    /// </summary>
    /// <remarks>
    /// A JSON object's keys are strings whatever the C# key type is, so
    /// <c>additionalProperties</c> alone describes a string-keyed map completely. An enum key is a
    /// closed set the server refuses anything outside of, and dropping it published a map
    /// accepting any key at all.
    /// </remarks>
    [Fact]
    public void AnEnumDictionaryKeyPublishesItsVocabulary() {
        var schema = ShapeDocument()
            .GetProperty("paths").GetProperty("/counts").GetProperty("get")
            .GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.Equal(
            "#/components/schemas/Priority",
            schema.GetProperty("propertyNames").GetProperty("$ref").GetString());
        Assert.Equal(
            "integer", schema.GetProperty("additionalProperties").GetProperty("type").GetString());
    }

    /// <summary>
    /// Two types published under one component name are reported.
    /// </summary>
    /// <remarks>
    /// A component is named by the type's own name, unqualified, which is the spelling client
    /// generators produce. Two types with that name in different controllers therefore collide and
    /// the merge keeps whichever arrived last, leaving one operation described with the other's
    /// shape in a document that is still valid.
    /// </remarks>
    [Fact]
    public void TwoTypesUnderOneComponentNameAreReported() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public class FirstController {
                public record Reading(string Sensor);

                [Get("/first")]
                public Reading Get() => new Reading("north");
            }

            public class SecondController {
                public record Reading(int Value, bool Settled);

                [Get("/second")]
                public Reading Get() => new Reading(1, true);
            }
            """, Enable));

        var diagnostic = Assert.Single(
            result.GeneratorDiagnostics,
            entry => entry.Id == Hardened.SourceGenerator.OpenApiDocument
                .OpenApiDocumentDiagnostics.SchemaNameCollisionId);

        var message = diagnostic.GetMessage();

        Assert.Contains("\"Reading\"", message);
        Assert.Contains("FirstController.Get", message);
        Assert.Contains("SecondController.Get", message);
    }

    /// <summary>
    /// Two types that write the same schema are not reported.
    /// </summary>
    /// <remarks>
    /// The document is then correct whichever one the merge keeps, so reporting it would name a
    /// defect no reader of the document can see.
    /// </remarks>
    [Fact]
    public void TwoIdenticalTypesUnderOneNameAreNotReported() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public class LeftController {
                public record Reading(string Sensor);

                [Get("/left")]
                public Reading Get() => new Reading("north");
            }

            public class RightController {
                public record Reading(string Sensor);

                [Get("/right")]
                public Reading Get() => new Reading("south");
            }
            """, Enable));

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            entry => entry.Id == Hardened.SourceGenerator.OpenApiDocument
                .OpenApiDocumentDiagnostics.SchemaNameCollisionId);
    }

    #endregion

    #region prose with commas in it

    /// <summary>
    /// CS-06. <c>Arguments.Split(',')</c> cut the description at its first comma, and the document
    /// published the truncation with nothing said.
    /// </summary>
    [Fact]
    public void ADescriptionKeepsItsCommas() {
        using var document = JsonDocument.Parse(Extract(Generate(
            Enable + "\n[OpenApiInfo(\"Depot\", \"1.0\", \"Parcels, pallets and freight\")]")));

        Assert.Equal(
            "Parcels, pallets and freight",
            document.RootElement.GetProperty("info").GetProperty("description").GetString());
    }

    /// <summary>And the title and version beside it are still their own arguments.</summary>
    [Fact]
    public void TheTitleAndVersionAreUnaffected() {
        using var document = JsonDocument.Parse(Extract(Generate(
            Enable + "\n[OpenApiInfo(\"Depot\", \"1.0\", \"Parcels, pallets and freight\")]")));

        var info = document.RootElement.GetProperty("info");

        Assert.Equal("Depot", info.GetProperty("title").GetString());
        Assert.Equal("1.0", info.GetProperty("version").GetString());
    }

    /// <summary>
    /// A description written as a named argument reaches the document under its own name rather
    /// than as the text "description: ...".
    /// </summary>
    [Fact]
    public void ANamedDescriptionIsRead() {
        using var document = JsonDocument.Parse(Extract(Generate(
            Enable + "\n[OpenApiInfo(\"Depot\", description: \"Parcels, pallets\")]")));

        Assert.Equal(
            "Parcels, pallets",
            document.RootElement.GetProperty("info").GetProperty("description").GetString());
    }

    /// <summary>
    /// A server's description carries its commas too - the same reading, and the one place that
    /// already split on the first comma only.
    /// </summary>
    [Fact]
    public void AServerDescriptionKeepsItsCommas() {
        using var document = JsonDocument.Parse(Extract(Generate(
            Enable + "\n[Server(\"https://api.example.com\", \"Production, and the only one\")]")));

        var server = document.RootElement.GetProperty("servers").EnumerateArray().First();

        Assert.Equal("https://api.example.com", server.GetProperty("url").GetString());
        Assert.Equal("Production, and the only one", server.GetProperty("description").GetString());
    }

    #endregion
}
