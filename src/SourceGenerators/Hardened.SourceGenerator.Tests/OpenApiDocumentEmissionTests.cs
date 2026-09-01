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
        Assert.Contains("OpenApiDocumentGZip", Generate(Enable));
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
        Assert.Contains("Application.OpenApiDocumentGZip", routing);
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
    private const string FidelityControllers = """
        public enum Carrier { Dhl, Fedex, RoyalMail }

        public record Shipment(string Id, int Quantity, string? Note, Carrier Carrier);

        public record NewShipment(
            [property: ValidationModules.Constraints.Range("0.5", "30", ExclusiveMin = true)]
            decimal WeightKg);

        public class ShipmentController {
            [Get("/shipments")]
            public Task<List<Shipment>> List(
                [FromQueryString] int limit = 20, [FromQueryString] Carrier? carrier = null) =>
                Task.FromResult(new List<Shipment>());

            [Post("/shipments")]
            public Task<Shipment> Create([FromBody] NewShipment body) =>
                Task.FromResult(new Shipment("1", 1, null, Carrier.Dhl));
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
        var schema = ListParameter(FidelityDocument(), "carrier").GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(
            new[] { "dhl", "fedex", "royalMail" },
            schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
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
}
