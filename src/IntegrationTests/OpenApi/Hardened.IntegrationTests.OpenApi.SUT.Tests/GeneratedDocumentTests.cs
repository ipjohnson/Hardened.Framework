using System.IO.Compression;
using System.Text.Json;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// What the generated document says, against what the contract said.
/// </summary>
/// <remarks>
/// <para>
/// The document a specification-first application publishes is generated from the normalised model,
/// and for a while it was a skeleton: paths, operation ids and tags, with no request body, no
/// response content and no <c>components</c> at all. <c>JsonSchemaWriter</c> builds schemas by
/// walking an <c>ITypeSymbol</c>, and a specification-first handler's payload types are written by
/// the build task rather than declared in the consumer's source - so every schema it would have
/// produced came from symbols this path does not have.
/// </para>
/// <para>
/// The prose went the same way and for a nearer-miss reason: the model carried every description
/// the contract wrote, <c>OpenApiDocumentGenerator</c> had always written
/// <c>handler.Summary</c> and <c>handler.Description</c>, and nothing on this path ever assigned
/// them. A document that describes no payloads and repeats none of the author's prose is valid
/// OpenAPI and no use to anyone reading it.
/// </para>
/// </remarks>
public class GeneratedDocumentTests {

    private static async Task<JsonElement> Document(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();
        response.Body.Position = 0;

        await using var gzip = new GZipStream(response.Body, CompressionMode.Decompress);

        return JsonDocument.Parse(await new StreamReader(gzip).ReadToEndAsync()).RootElement;
    }

    private static JsonElement CreatePet(JsonElement document) =>
        document.GetProperty("paths").GetProperty("/pets").GetProperty("post");

    [HardenedTest]
    public async Task TheOperationCarriesTheSummaryAndDescriptionSeparately(ITestWebApp app) {
        var post = CreatePet(await Document(app));

        // Two fields, because a document has two and they render differently. They used to be
        // collapsed at parse time with the summary winning, which put a title in the description
        // and discarded the prose.
        Assert.Equal("Add a pet to the store", post.GetProperty("summary").GetString());
        Assert.Contains("The prose a description carries", post.GetProperty("description").GetString());
    }

    [HardenedTest]
    public async Task TheOperationCarriesItsRequestBodySchema(ITestWebApp app) {
        var schema = CreatePet(await Document(app))
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");

        Assert.Equal("#/components/schemas/CreatePetRequest", schema.GetProperty("$ref").GetString());
    }

    /// <summary>
    /// The declared status, with the payload declared for it - not a bare 200.
    /// </summary>
    [HardenedTest]
    public async Task TheOperationCarriesItsDeclaredResponse(ITestWebApp app) {
        var created = CreatePet(await Document(app)).GetProperty("responses").GetProperty("201");

        Assert.Equal("Pet created", created.GetProperty("description").GetString());

        Assert.Equal(
            "#/components/schemas/Pet",
            created.GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString());
    }

    [HardenedTest]
    public async Task TheComponentsCarryTheSchemasTheContractDeclared(ITestWebApp app) {
        var schemas = (await Document(app)).GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("Pet", out var pet));
        Assert.True(schemas.TryGetProperty("CreatePetRequest", out _));

        Assert.Equal("A pet in the store, as the store knows it.", pet.GetProperty("description").GetString());

        Assert.Equal(
            "Assigned by the store when the pet is created.",
            pet.GetProperty("properties").GetProperty("id").GetProperty("description").GetString());

        Assert.Equal("id", pet.GetProperty("required")[0].GetString());
    }

    [HardenedTest]
    public async Task AParameterCarriesItsDescription(ITestWebApp app) {
        var parameter = (await Document(app))
            .GetProperty("paths").GetProperty("/pets/{petId}").GetProperty("get")
            .GetProperty("parameters")[0];

        Assert.Equal("petId", parameter.GetProperty("name").GetString());
        Assert.Equal(
            "The pet's identifier, as assigned by the server.",
            parameter.GetProperty("description").GetString());
    }
}
