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
    /// <remarks>
    /// 3.2.0 is the default, and this application sets no <c>&lt;HardenedOpenApiVersion&gt;</c>, so
    /// what is asserted here is the default reaching the document rather than a value being
    /// honoured - <c>OpenApiVersionTests</c> covers that.
    /// </remarks>
    [HardenedTest]
    public async Task TheDocumentIsValidOpenApi(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        Assert.Equal("3.2.0", document.RootElement.GetProperty("openapi").GetString());
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

    /// <summary>
    /// A path template names its parameters and nothing else.
    /// </summary>
    /// <remarks>
    /// Route constraints and the catch-all marker used to survive into the template, so the
    /// document declared <c>/binding/path-constrained/{count:int}</c>. The name there then
    /// disagreed with the name in <c>parameters</c>, which is a <c>path-params</c> error to a
    /// linter and a request for <c>/boards/%7BboardId:guid%7D</c> from a generated client.
    /// </remarks>
    [HardenedTest]
    public async Task PathTemplatesCarryNoRoutingSyntax(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        var leaked = document.RootElement.GetProperty("paths").EnumerateObject()
            .Select(path => path.Name)
            .Where(path => path.Contains(':') || path.Contains('*'))
            .ToList();

        Assert.True(leaked.Count == 0,
            "path templates carry routing syntax a document cannot express: " + string.Join(", ", leaked));

        // The constrained route is still there, under the name its parameter is declared with.
        Assert.True(document.RootElement.GetProperty("paths")
            .TryGetProperty("/binding/path-constrained/{count}", out _));
    }

    /// <summary>
    /// A parameter is typed as the handler declared it, not as the text it arrived in.
    /// </summary>
    /// <remarks>
    /// Every parameter was written as a string whatever its C# type, so a document described
    /// <c>ConstrainedPathToken(int count)</c> as taking a string — and a typed client had no reason
    /// to reject <c>/path-constrained/abc</c> before sending it.
    /// </remarks>
    [HardenedTest]
    public async Task ParametersCarryTheirDeclaredType(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        var schema = document.RootElement.GetProperty("paths")
            .GetProperty("/binding/path-constrained/{count}")
            .GetProperty("get").GetProperty("parameters")[0]
            .GetProperty("schema");

        Assert.Equal("integer", schema.GetProperty("type").GetString());
        Assert.Equal("int32", schema.GetProperty("format").GetString());
    }

    /// <summary>
    /// The document declares the groups its operations reference.
    /// </summary>
    /// <remarks>
    /// Operations carried tags and nothing declared them, which is legal and lossy: the top-level
    /// list is where a tag's order is set, so a reader and a generated SDK grouped by whatever the
    /// names sorted to rather than by what the application declared.
    /// </remarks>
    [HardenedTest]
    public async Task TheDocumentDeclaresItsTags(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        Assert.True(document.RootElement.TryGetProperty("tags", out var tags),
            "the document declares no tags, so its operations reference groups it never defines");

        var declared = tags.EnumerateArray().Select(tag => tag.GetProperty("name").GetString()).ToList();

        Assert.Contains("Registration", declared);
        Assert.Contains("Binding", declared);

        // Every tag an operation uses has to be one of these.
        var used = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .SelectMany(operation => operation.Value.GetProperty("tags").EnumerateArray())
            .Select(tag => tag.GetString())
            .Distinct();

        Assert.All(used, tag => Assert.Contains(tag, declared));
    }

    /// <summary>
    /// A handler carrying a validation filter still describes its body and its response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The regression guard for the defect that produced two of these findings at once. Attaching
    /// a validator rebuilt the handler model by hand and carried two of its eight settable
    /// properties, dropping both OpenAPI schemas — so the operations with the most carefully
    /// specified models were the ones documented as accepting nothing and returning nothing.
    /// </para>
    /// <para>
    /// Asserted here rather than in the generator's own round-trip tests because those deliberately
    /// do not run the validation generator, and this defect only exists once it has run.
    /// <c>RegistrationController</c> is the handler set that gets validators.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task AValidatedHandlerStillDescribesItsBodyAndResponse(ITestWebApp testWebApp) {
        using var document = await Fetch(testWebApp);

        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/registration").GetProperty("post");

        var body = operation.GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.Equal("#/components/schemas/RegistrationModel", body.GetProperty("$ref").GetString());

        var response = operation.GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.Equal("string", response.GetProperty("type").GetString());
    }
}
