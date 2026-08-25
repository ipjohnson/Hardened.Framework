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
    /// And the status the model declares, rather than a guess.
    /// </summary>
    [HardenedTest]
    public async Task TheServedDocumentCarriesTheDeclaredStatus(ITestWebApp app) {
        var responses = (await Document(app))
            .GetProperty("paths").GetProperty("/pets").GetProperty("post").GetProperty("responses");

        Assert.True(responses.TryGetProperty("201", out _));
    }
}
