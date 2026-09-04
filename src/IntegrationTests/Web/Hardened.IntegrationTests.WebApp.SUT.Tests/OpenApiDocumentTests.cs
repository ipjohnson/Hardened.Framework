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

        return JsonDocument.Parse(await response.ReadTextAsync());
    }

    /// <summary>
    /// It is served compressed, which is the form it is embedded in.
    /// </summary>
    /// <remarks>
    /// The document is gzipped by the generator and the bytes go out untouched for a client that
    /// says it takes them - which is every client - so the common path compresses nothing per
    /// request. <c>TestWebApp</c> sends <c>Accept-Encoding: gzip</c> on every request, so this is
    /// that path.
    /// </remarks>
    [HardenedTest]
    public async Task TheDocumentIsServedCompressed(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.json");

        response.Assert.Ok();

        Assert.Equal("gzip", response.Headers["Content-Encoding"].ToString());
        Assert.Equal(
            response.Body.Length.ToString(), response.Headers["Content-Length"].ToString());
    }

    /// <summary>
    /// A client that does not take gzip gets the document inflated rather than unreadable.
    /// </summary>
    [HardenedTest]
    public async Task AClientThatDoesNotAcceptGZipGetsPlainJson(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/openapi.json", request => request.Headers["Accept-Encoding"] = "identity");

        response.Assert.Ok();

        Assert.False(response.Headers.ContainsKey("Content-Encoding"));

        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body, leaveOpen: true);

        using var document = JsonDocument.Parse(await reader.ReadToEndAsync());

        Assert.Equal("3.2.0", document.RootElement.GetProperty("openapi").GetString());
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

        // Values is declared List<int>?, and the schema now says so: "null" beside "array"
        // rather than a nullable member described as always present.
        Assert.Equal(
            new[] { "array", "null" },
            schema.GetProperty("properties").GetProperty("values").GetProperty("type")
                .EnumerateArray().Select(value => value.GetString()));
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

    #region a collection parameter

    private static async Task<JsonElement> ParameterSchema(
        ITestWebApp testWebApp, string path, string name) {
        using var document = await Fetch(testWebApp);

        var parameter = document.RootElement
            .GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("parameters")
            .EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);

        return parameter.GetProperty("schema").Clone();
    }

    /// <summary>
    /// A code-first collection parameter is an array in the document. The schema is read off the C#
    /// type, whose fall-through is <c>string</c>, so a <c>List</c> published as a string - the
    /// binder filling it from every value the request carried while the document described one.
    /// </summary>
    [HardenedTest]
    public async Task ACollectionQueryParameterIsAnArray(ITestWebApp testWebApp) {
        var schema = await ParameterSchema(testWebApp, "/binding/query-list", "symbols");

        Assert.Equal("array", schema.GetProperty("type").GetString());
        Assert.Equal("string", schema.GetProperty("items").GetProperty("type").GetString());
    }

    /// <summary>The item type is the one the handler declared, not a default.</summary>
    [HardenedTest]
    public async Task ACollectionParametersItemsCarryTheirOwnType(ITestWebApp testWebApp) {
        var schema = await ParameterSchema(testWebApp, "/binding/query-list-typed", "ids");

        Assert.Equal("array", schema.GetProperty("type").GetString());
        Assert.Equal("integer", schema.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("int32", schema.GetProperty("items").GetProperty("format").GetString());
    }

    [HardenedTest]
    public async Task AnArrayParameterIsAnArray(ITestWebApp testWebApp) {
        var schema = await ParameterSchema(testWebApp, "/binding/query-array", "tags");

        Assert.Equal("array", schema.GetProperty("type").GetString());
        Assert.Equal("string", schema.GetProperty("items").GetProperty("type").GetString());
    }

    [HardenedTest]
    public async Task ACollectionHeaderParameterIsAnArray(ITestWebApp testWebApp) {
        var schema = await ParameterSchema(testWebApp, "/binding/header-list", "X-Tag");

        Assert.Equal("array", schema.GetProperty("type").GetString());
    }

    /// <summary>And a scalar parameter is still a scalar.</summary>
    [HardenedTest]
    public async Task AScalarQueryParameterIsNotAnArray(ITestWebApp testWebApp) {
        var schema = await ParameterSchema(testWebApp, "/binding/query-typed", "page");

        Assert.Equal("integer", schema.GetProperty("type").GetString());
    }

    #endregion

    #region the declared validation status

    private static async Task<string[]> Statuses(ITestWebApp testWebApp, string path) {
        using var document = await Fetch(testWebApp);

        return document.RootElement
            .GetProperty("paths").GetProperty(path).GetProperty("post").GetProperty("responses")
            .EnumerateObject().Select(status => status.Name).OrderBy(name => name).ToArray();
    }

    /// <summary>
    /// The operation publishes the status it answers, and only that one. The trial's code-first arm
    /// declared 422 by hand and the document carried the synthesized 400 beside it - a status the
    /// operation could no longer produce.
    /// </summary>
    [HardenedTest]
    public async Task AnOperationDeclaring422PublishesItAndNoSynthesized400(ITestWebApp testWebApp) {
        var statuses = await Statuses(testWebApp, "/registration/declared-422");

        Assert.Contains("422", statuses);
        Assert.DoesNotContain("400", statuses);
    }

    /// <summary>
    /// And an operation that declares nothing still publishes the 400 it answers, which is what the
    /// synthesis is for.
    /// </summary>
    [HardenedTest]
    public async Task AnOperationDeclaringNothingStillPublishesThe400(ITestWebApp testWebApp) {
        Assert.Contains("400", await Statuses(testWebApp, "/registration/for/{tenant}"));
    }

    #endregion
}
