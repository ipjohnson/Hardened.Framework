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

    /// <summary>
    /// The constraints the contract declares reach the published schemas.
    /// </summary>
    /// <remarks>
    /// The model carried every one of these facts and the writer published none - only type,
    /// format and description survived, while parameters travelled a function that always knew
    /// the keywords. The trial's document-fidelity matrix found exactly that inversion: every
    /// parameter constraint published, every body constraint dropped. These assertions are that
    /// matrix, over the schemas this contract already declares.
    /// </remarks>
    [HardenedTest]
    public async Task TheSchemasCarryTheConstraintsTheContractDeclared(ITestWebApp app) {
        var schemas = (await Document(app)).GetProperty("components").GetProperty("schemas");

        var create = schemas.GetProperty("CreatePetRequest").GetProperty("properties");

        Assert.Equal(1, create.GetProperty("name").GetProperty("minLength").GetInt32());
        Assert.Equal(100, create.GetProperty("name").GetProperty("maxLength").GetInt32());
        Assert.Equal(
            "^[a-zA-Z0-9-]*$", create.GetProperty("tag").GetProperty("pattern").GetString());

        var rating = schemas.GetProperty("UpdatePetRequest").GetProperty("properties")
            .GetProperty("rating");

        Assert.Equal(1, rating.GetProperty("minimum").GetInt32());
        Assert.Equal(5, rating.GetProperty("maximum").GetInt32());
    }

    /// <summary>
    /// The description the contract's top-level tag declaration carries reaches the published
    /// tags list, which used to carry the name alone.
    /// </summary>
    [HardenedTest]
    public async Task TheTagCarriesItsDeclaredDescription(ITestWebApp app) {
        var tags = (await Document(app)).GetProperty("tags");

        var pet = tags.EnumerateArray()
            .Single(tag => tag.GetProperty("name").GetString() == "Pet");

        Assert.Equal(
            "Everything about the pets in the store.",
            pet.GetProperty("description").GetString());
    }

    /// <summary>
    /// A property the contract marks nullable publishes the 2020-12 type array, the way the
    /// code-first writer already does. The framework's own 404 body sends <c>detail</c> as null.
    /// </summary>
    [HardenedTest]
    public async Task ANullablePropertyPublishesTheTypeArray(ITestWebApp app) {
        var detail = (await Document(app)).GetProperty("components").GetProperty("schemas")
            .GetProperty("Problem").GetProperty("properties").GetProperty("detail");

        var type = detail.GetProperty("type");

        Assert.Equal(JsonValueKind.Array, type.ValueKind);
        Assert.Equal(
            new[] { "string", "null" },
            type.EnumerateArray().Select(entry => entry.GetString()).ToArray());
    }

    /// <summary>
    /// Every verb declared at one path reaches the document, including where they disagree about
    /// the token's constraint.
    /// </summary>
    /// <remarks>
    /// The document keys its paths on the template and the router keys its routes on the route, and
    /// the two differ wherever a token carries a constraint - <c>ToTemplate</c> strips it. Grouping
    /// on the route wrote <c>"/pets/{petId}"</c> twice, and every JSON parser keeps the last, so the
    /// GET declared beside a constrained DELETE was absent from the document while continuing to
    /// serve. Nothing failed: the document parsed, the route worked, and only a client reading the
    /// description was wrong about the API.
    /// </remarks>
    [HardenedTest]
    public async Task EveryVerbAtOnePathReachesTheDocument(ITestWebApp app) {
        var item = (await Document(app)).GetProperty("paths").GetProperty("/pets/{petId}");

        foreach (var verb in new[] { "get", "delete", "patch", "put" }) {
            Assert.True(item.TryGetProperty(verb, out _), $"the document has no {verb} at /pets/{{petId}}");
        }
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

    #region what the trial found missing

    /// <summary>
    /// The contract's own info block, not the module class name and "1.0.0" the generator used to
    /// substitute for it.
    /// </summary>
    [HardenedTest]
    public async Task TheContractsInfoBlockIsServed(ITestWebApp app) {
        var info = (await Document(app)).GetProperty("info");

        Assert.Equal("Petstore API", info.GetProperty("title").GetString());
        Assert.Equal("1.0.0", info.GetProperty("version").GetString());
    }

    /// <summary>
    /// The oauth2 scheme the contract declares, flows and scopes included, and the requirement on
    /// the operation that names it. Nothing was published, so a generated client sent every
    /// request anonymous and had no token URL to go to.
    /// </summary>
    [HardenedTest]
    public async Task TheDeclaredSchemeAndScopesAreServed(ITestWebApp app) {
        var document = await Document(app);

        var scheme = document
            .GetProperty("components").GetProperty("securitySchemes").GetProperty("petstoreOAuth");

        Assert.Equal("oauth2", scheme.GetProperty("type").GetString());
        Assert.True(scheme.GetProperty("flows").GetProperty("clientCredentials")
            .GetProperty("scopes").TryGetProperty("pets:read", out _));

        var secured = document
            .GetProperty("paths").GetProperty("/secured/scoped").GetProperty("get");
        var requirement = Assert.Single(secured.GetProperty("security").EnumerateArray());

        Assert.Equal(
            "pets:read",
            Assert.Single(requirement.GetProperty("petstoreOAuth").EnumerateArray()).GetString());
    }

    /// <summary>The Location the 201 declares and the service sends, as a headers block.</summary>
    [HardenedTest]
    public async Task TheDeclaredResponseHeaderIsServed(ITestWebApp app) {
        var created = CreatePet(await Document(app))
            .GetProperty("responses").GetProperty("201");

        var location = created.GetProperty("headers").GetProperty("Location");

        Assert.Equal("string", location.GetProperty("schema").GetProperty("type").GetString());
    }

    /// <summary>
    /// The 400 the generated validator answers, declared with its body's schema. Every constraint
    /// failure was a status the document never mentioned.
    /// </summary>
    [HardenedTest]
    public async Task TheValidationResponseIsDeclared(ITestWebApp app) {
        var document = await Document(app);

        var badRequest = CreatePet(document).GetProperty("responses").GetProperty("400");

        Assert.Equal(
            "#/components/schemas/RequestValidationError",
            badRequest.GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString());
    }

    #endregion
}
