using System.Text.Json;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The document this application generates from its own attribute routes, served over HTTP.
/// </summary>
/// <remarks>
/// The reverse of everything else here: the specification-first generator turns a document into
/// code, and this turns code into a document — so an attribute-routed application can hand a client
/// the same contract a specification-first one starts from.
/// </remarks>
public class OpenApiDocumentTests {

    private static async Task<JsonDocument> Fetch(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.json");

        response.Assert.Ok();

        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return JsonDocument.Parse(await reader.ReadToEndAsync());
    }

    [HardenedTest]
    public async Task TheDocumentIsServedAsJson(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.json");

        response.Assert.Ok();

        Assert.Equal("application/json", response.Headers["Content-Type"]);
    }

    /// <summary>It is a document, not a JSON-encoded string of one.</summary>
    [HardenedTest]
    public async Task TheDocumentIsValidOpenApi(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        Assert.Equal("3.0.0", document.RootElement.GetProperty("openapi").GetString());
        Assert.True(document.RootElement.TryGetProperty("paths", out _));
    }

    /// <summary>Routes declared with attributes appear, at the paths they are served from.</summary>
    [HardenedTest]
    public async Task DeclaredRoutesAppearInTheDocument(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/binding/path/{id}", out var withToken));
        Assert.True(withToken.TryGetProperty("get", out _));

        Assert.True(paths.TryGetProperty("/verbs/item/{id}", out var verbs));
        foreach (var verb in new[] { "get", "delete", "patch" }) {
            Assert.True(verbs.TryGetProperty(verb, out _), verb + " missing");
        }
    }

    /// <summary>A path token is described as a path parameter, not a query one.</summary>
    [HardenedTest]
    public async Task ParametersCarryTheirLocation(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        var parameters = document.RootElement
            .GetProperty("paths").GetProperty("/binding/mixed/{id}")
            .GetProperty("get").GetProperty("parameters");

        var byName = parameters.EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("in").GetString());

        Assert.Equal("path", byName["id"]);
        Assert.Equal("query", byName["filter"]);
        Assert.Equal("header", byName["X-Tenant"]);
    }

    /// <summary>
    /// A request body is described by a schema referencing a real component, which is the part that
    /// needed the type walked while its symbol still existed.
    /// </summary>
    [HardenedTest]
    public async Task ABodyIsDescribedByAGeneratedSchema(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        var reference = document.RootElement
            .GetProperty("paths").GetProperty("/int/add").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString();

        Assert.Equal("#/components/schemas/MathAddModel", reference);

        var schema = document.RootElement
            .GetProperty("components").GetProperty("schemas").GetProperty("MathAddModel");

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("array", schema.GetProperty("properties").GetProperty("values").GetProperty("type").GetString());
    }
}
