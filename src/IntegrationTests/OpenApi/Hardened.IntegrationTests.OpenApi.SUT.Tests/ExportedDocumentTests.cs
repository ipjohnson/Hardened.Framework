using Hardened.Generation.Document;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// The file the build exported is the document the application serves.
/// </summary>
/// <remarks>
/// <para>
/// <c>&lt;HardenedOpenApiOutput&gt;</c> on the application project writes <c>openapi/OpenApiTestApp.json</c>
/// after every compile, read out of the compiled assembly rather than out of generated source or
/// a running application. This holds the two ends to each other: the served document, fetched
/// through the pipeline and inflated, indented the way the export indents, is the file byte for
/// byte. A client generated from the file is therefore generated from what the server implements.
/// </para>
/// <para>
/// The file is tracked. A route change that is built and not committed shows up as a diff, and
/// this test fails on a checkout whose file is stale.
/// </para>
/// </remarks>
public class ExportedDocumentTests {

    private static string Exported() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi", "OpenApiTestApp.json"));

    [HardenedTest]
    public async Task TheExportedFileIsTheServedDocument(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();

        var served = JsonTreeWriter.WriteIndented(JsonTree.Parse(await response.ReadTextAsync()));

        Assert.Equal(served, Exported());
    }

    /// <summary>The file is what a reviewer and a generator read: indented, not the compact literal.</summary>
    [HardenedTest]
    public async Task TheExportedFileIsIndented(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();

        var compact = await response.ReadTextAsync();
        var exported = Exported();

        Assert.StartsWith("{\n  \"openapi\": ", exported);
        Assert.True(exported.Length > compact.Length);
        Assert.EndsWith("\n", exported);
    }
}
