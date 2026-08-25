namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The doc comments this application writes, in the document it publishes.
/// </summary>
/// <remarks>
/// <para>
/// <b>This project deliberately does not set <c>GenerateDocumentationFile</c>.</b> That is the
/// point of the test. <c>XmlDocumentation</c>'s own remarks said it read syntax rather than
/// <c>GetDocumentationCommentXml</c> so the document would not depend on whether a project emits an
/// XML file - and reading structured trivia has exactly that dependency, because Roslyn only builds
/// a <c>DocumentationCommentTriviaSyntax</c> when the parse options ask it to. A fully documented
/// application published a document with no prose in it, and nothing said why.
/// </para>
/// <para>
/// Setting the flag was not a fix worth having either: it reported CS1591 for every public member
/// the generators emit - 1,436 warnings against this application, 560 from one generated links file
/// - which buries the signal the flag exists to give.
/// </para>
/// </remarks>
public class GeneratedDocumentProseTests {

    private static async Task<JsonElement> Document(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();
        response.Body.Position = 0;

        await using var gzip = new System.IO.Compression.GZipStream(
            response.Body, System.IO.Compression.CompressionMode.Decompress);

        return JsonDocument.Parse(await new StreamReader(gzip).ReadToEndAsync()).RootElement;
    }

    [HardenedTest]
    public async Task AHandlersSummaryReachesTheOperation(ITestWebApp app) {
        var operation = (await Document(app))
            .GetProperty("paths").GetProperty("/authorization/unguarded").GetProperty("get");

        Assert.Equal(
            "No attribute at all, which is public while nothing has opted in.",
            operation.GetProperty("summary").GetString());
    }

    [HardenedTest]
    public async Task AParamTagReachesTheParameter(ITestWebApp app) {
        var parameter = (await Document(app))
            .GetProperty("paths").GetProperty("/binding/path/{id}").GetProperty("get")
            .GetProperty("parameters")[0];

        Assert.Equal("id", parameter.GetProperty("name").GetString());
        Assert.Equal(
            "The token to echo, taken from the path.",
            parameter.GetProperty("description").GetString());
    }

    [HardenedTest]
    public async Task ATypesSummaryReachesItsSchema(ITestWebApp app) {
        var schema = (await Document(app))
            .GetProperty("components").GetProperty("schemas").GetProperty("RegistrationModel");

        Assert.Contains("A body model with constraints on it", schema.GetProperty("description").GetString());
    }

    [HardenedTest]
    public async Task APropertysSummaryReachesItsSchema(ITestWebApp app) {
        var name = (await Document(app))
            .GetProperty("components").GetProperty("schemas").GetProperty("RegistrationModel")
            .GetProperty("properties").GetProperty("name");

        Assert.Equal("The name the registration is filed under.", name.GetProperty("description").GetString());
    }
}
