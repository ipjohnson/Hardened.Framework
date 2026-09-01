using System.IO.Compression;
using System.Text.Json;

namespace Hardened.IntegrationTests.Smithy.SUT.Tests;

/// <summary>
/// What a Smithy application serves when it opts in to publishing a document.
/// </summary>
/// <remarks>
/// <para>
/// An OpenAPI document, generated from the normalised model - the same function, over the same
/// models, that an attribute-routed application uses. It used to be the contract itself, served
/// verbatim, which is an OpenAPI document only when the contract happens to be one. A Smithy model
/// published a Smithy 2.0 AST at a path named <c>openapi.json</c>: unreadable by every OpenAPI
/// client, not where any Smithy tool would look, and the reason the reference page over it rendered
/// zero operations.
/// </para>
/// <para>
/// Publishing is still opt-in. What changed is what opting in gets you.
/// </para>
/// </remarks>
public class SmithyServedDocumentTests {

    private static async Task<JsonElement> Document(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();

        response.Body.Position = 0;

        await using var gzip = new GZipStream(response.Body, CompressionMode.Decompress);

        return JsonDocument.Parse(await new StreamReader(gzip).ReadToEndAsync()).RootElement;
    }

    [HardenedTest]
    public async Task TheServedDocumentIsOpenApiAndNotASmithyAst(ITestWebApp app) {
        var document = await Document(app);

        Assert.True(document.TryGetProperty("openapi", out var version));
        Assert.StartsWith("3.", version.GetString());

        // The AST's own marker, which is what used to be served here.
        Assert.False(document.TryGetProperty("smithy", out _));
        Assert.False(document.TryGetProperty("shapes", out _));
    }

    [HardenedTest]
    public async Task TheServedDocumentCarriesTheRoutesTheModelDeclares(ITestWebApp app) {
        var paths = (await Document(app)).GetProperty("paths");

        Assert.True(paths.TryGetProperty("/pets", out var pets));
        Assert.True(pets.TryGetProperty("get", out _));
        Assert.True(pets.TryGetProperty("post", out _));

        Assert.True(paths.TryGetProperty("/pets/{petId}", out var pet));
        Assert.True(pet.TryGetProperty("get", out _));
    }

    /// <summary>
    /// The <c>@length</c> the model declares on a body member reaches the published schema. Both
    /// spec front ends share the writer that dropped every body constraint, so this is the Smithy
    /// half of the same fix the OpenAPI SUT asserts.
    /// </summary>
    [HardenedTest]
    public async Task TheBodySchemasCarryTheConstraintsTheModelDeclares(ITestWebApp app) {
        var document = await Document(app);

        var bodyRef = document.GetProperty("paths").GetProperty("/pets").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();

        var name = document.GetProperty("components").GetProperty("schemas")
            .GetProperty(bodyRef!.Substring("#/components/schemas/".Length))
            .GetProperty("properties").GetProperty("name");

        Assert.Equal(1, name.GetProperty("minLength").GetInt32());
        Assert.Equal(64, name.GetProperty("maxLength").GetInt32());
    }

    /// <summary>
    /// An enum bound as a query value publishes its vocabulary. It published a bare string:
    /// Smithy's <c>Describe()</c> returns the shape's reference and nothing else, and the builder
    /// never resolved it, so the writer fell back to the C# type.
    /// </summary>
    [HardenedTest]
    public async Task AnEnumQueryParameterPublishesItsVocabulary(ITestWebApp app) {
        var parameters = (await Document(app)).GetProperty("paths").GetProperty("/pets")
            .GetProperty("get").GetProperty("parameters");

        var kind = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "kind");

        var schema = kind.GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(
            new[] { "dog", "cat", "other" },
            schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// A member that is neither <c>@required</c> nor defaulted is nullable under Smithy's rules,
    /// and the published schema says so with the 2020-12 type array.
    /// </summary>
    [HardenedTest]
    public async Task AnOptionalMemberPublishesTheNullableTypeArray(ITestWebApp app) {
        var document = await Document(app);

        var responseRef = document.GetProperty("paths").GetProperty("/pets").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();

        var nextToken = document.GetProperty("components").GetProperty("schemas")
            .GetProperty(responseRef!.Substring("#/components/schemas/".Length))
            .GetProperty("properties").GetProperty("nextToken");

        var type = nextToken.GetProperty("type");

        Assert.Equal(JsonValueKind.Array, type.ValueKind);
        Assert.Equal(
            new[] { "string", "null" },
            type.EnumerateArray().Select(entry => entry.GetString()).ToArray());
    }

    /// <summary>
    /// And the status the model declares, rather than a guess.
    /// </summary>
    [HardenedTest]
    public async Task TheServedDocumentCarriesTheDeclaredStatus(ITestWebApp app) {
        var responses = (await Document(app))
            .GetProperty("paths").GetProperty("/pets").GetProperty("post").GetProperty("responses");

        Assert.True(responses.TryGetProperty("201", out _));
    }

    /// <summary>
    /// The model's own identity: @title and the service version, not the module class name and
    /// "1.0.0" the generator used to substitute.
    /// </summary>
    [HardenedTest]
    public async Task TheServedDocumentCarriesTheModelsIdentity(ITestWebApp app) {
        var info = (await Document(app)).GetProperty("info");

        Assert.Equal("Pet Store", info.GetProperty("title").GetString());
        Assert.Equal("2024-01-01", info.GetProperty("version").GetString());
    }

    /// <summary>
    /// The scheme the service enforces is declared, and the operations split exactly as the model
    /// splits them: the secured operation names its requirement, an @auth([]) one declares none.
    /// A generated client used to be told nothing and sent every request anonymous.
    /// </summary>
    [HardenedTest]
    public async Task TheServedDocumentDeclaresTheEnforcedScheme(ITestWebApp app) {
        var document = await Document(app);

        var scheme = document
            .GetProperty("components").GetProperty("securitySchemes").GetProperty("httpBearerAuth");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        var secured = document
            .GetProperty("paths").GetProperty("/pets/secured").GetProperty("get");
        var requirement = Assert.Single(secured.GetProperty("security").EnumerateArray());

        Assert.Equal(0, requirement.GetProperty("httpBearerAuth").GetArrayLength());

        var open = document.GetProperty("paths").GetProperty("/pets").GetProperty("get");

        Assert.False(open.TryGetProperty("security", out _));
    }

    /// <summary>
    /// The declared bounds and pattern the validators enforce, on the parameters that declare
    /// them. Every query and header parameter was published as a bare string.
    /// </summary>
    [HardenedTest]
    public async Task TheServedDocumentCarriesParameterFacts(ITestWebApp app) {
        var document = await Document(app);

        var parameters = document
            .GetProperty("paths").GetProperty("/pets").GetProperty("get")
            .GetProperty("parameters");

        foreach (var parameter in parameters.EnumerateArray()) {
            if (parameter.GetProperty("name").GetString() != "limit") {
                continue;
            }

            var schema = parameter.GetProperty("schema");

            Assert.Equal("integer", schema.GetProperty("type").GetString());
            Assert.Equal(1, schema.GetProperty("minimum").GetDecimal());
            Assert.Equal(100, schema.GetProperty("maximum").GetDecimal());

            return;
        }

        Assert.Fail("No limit parameter in the served document.");
    }
}
