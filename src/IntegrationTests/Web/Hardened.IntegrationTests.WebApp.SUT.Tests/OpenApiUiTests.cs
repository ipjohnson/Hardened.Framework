using System.Text.Json;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The reference page, served through the ordinary pipeline.
/// </summary>
/// <remarks>
/// <para>
/// It is a generated route rather than an <c>IWebExecutionRequestHandlerProvider</c>, and that is
/// the design rather than an implementation detail: conventions are applied while a generated
/// handler's filter chain is built, so a provider serving its own chain - which is what the health
/// endpoints and the document endpoint both do - is invisible to an
/// <c>IAuthorizationConvention</c>. This is not.
/// </para>
/// <para>
/// This application applies <c>[HardenedOpenApiUi(Title = "Integration Tests")]</c>, so what is
/// asserted here is the whole arrangement: the module attribute reaching configuration,
/// configuration reaching the model, and the model reaching the page.
/// </para>
/// </remarks>
public class OpenApiUiTests {

    [HardenedTest]
    public async Task TheUiIsServedAsHtml(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/docs");

        response.Assert.Ok();

        Assert.Equal("text/html; charset=utf-8", response.Headers["Content-Type"].ToString());
    }

    /// <summary>
    /// The title set on the module attribute reaches the page, which is the only evidence that the
    /// property survived the generated attribute at all - a non-nullable one would have arrived null.
    /// </summary>
    [HardenedTest]
    public async Task TheConfiguredTitleReachesThePage(ITestWebApp testWebApp) {
        var page = await (await testWebApp.Get("/docs")).ReadTextAsync();

        Assert.Contains("<title>Integration Tests</title>", page);
    }

    /// <summary>
    /// And the defaults it did not set reach it too, rather than being blanked by the attribute.
    /// </summary>
    [HardenedTest]
    public async Task TheUnsetDefaultsReachThePage(ITestWebApp testWebApp) {
        var page = await (await testWebApp.Get("/docs")).ReadTextAsync();

        Assert.Contains("data-url=\"/openapi.json\"", page);
        Assert.Contains("@scalar/api-reference@", page);
        Assert.Contains("integrity=\"sha384-", page);
        Assert.Contains("crossorigin=\"anonymous\"", page);
    }

    /// <summary>
    /// The page points at a document that is actually served. A reference page rendering against a
    /// 404 is the failure this pairing exists to avoid, and nothing in the type system prevents it.
    /// </summary>
    [HardenedTest]
    public async Task ThePageReferencesADocumentThisApplicationServes(ITestWebApp testWebApp) {
        var page = await (await testWebApp.Get("/docs")).ReadTextAsync();

        var start = page.IndexOf("data-url=\"", StringComparison.Ordinal) + "data-url=\"".Length;
        var documentPath = page.Substring(start, page.IndexOf('"', start) - start);

        var document = await testWebApp.Get(documentPath);

        document.Assert.Ok();

        using var parsed = JsonDocument.Parse(await document.ReadTextAsync());

        Assert.True(parsed.RootElement.TryGetProperty("openapi", out _));
    }

    /// <summary>
    /// The page is not an operation in the document it renders.
    /// </summary>
    /// <remarks>
    /// It is a real route, so it would be - except that it is declared in <c>Hardened.Web.Runtime</c>
    /// rather than here, and a routing table is generated per compilation from the handlers in it.
    /// The library ships its own table as one more provider, and this application's document
    /// describes this application.
    /// </remarks>
    [HardenedTest]
    public async Task ThePageDoesNotAppearInTheDocument(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.json");

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        Assert.False(document.RootElement.GetProperty("paths").TryGetProperty("/docs", out _));
    }

    /// <summary>
    /// A verb the page does not answer is a 405 rather than a 404: the resource exists.
    /// </summary>
    [HardenedTest]
    public async Task AWriteToThePageIsMethodNotAllowed(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { }, "/docs");

        Assert.Equal(405, response.StatusCode);
    }

    /// <summary>
    /// A second page, installed alongside the first, at its own path and against its own document.
    /// </summary>
    /// <remarks>
    /// This is what <c>HardenedOpenApiUi.Equals</c> being keyed on <c>Path</c> buys. DependencyModules
    /// loads a module once per distinct value of its equality, so type-only equality - the generated
    /// default - would have collapsed these two into one and the second page would not exist. A
    /// service publishing several specifications wants one page for each.
    /// </remarks>
    [HardenedTest]
    public async Task ASecondPageIsServedAtItsOwnPath(ITestWebApp testWebApp) {
        var page = await (await testWebApp.Get("/docs/internal")).ReadTextAsync();

        Assert.Contains("<title>Internal</title>", page);
        Assert.Contains("data-url=\"/internal.json\"", page);
    }

    /// <summary>
    /// And the two do not bleed into each other. Each provider holds its own configuration, because
    /// one registered in the container would be whichever module ran last.
    /// </summary>
    [HardenedTest]
    public async Task EachPageRendersItsOwnConfiguration(ITestWebApp testWebApp) {
        var first = await (await testWebApp.Get("/docs")).ReadTextAsync();
        var second = await (await testWebApp.Get("/docs/internal")).ReadTextAsync();

        Assert.Contains("<title>Integration Tests</title>", first);
        Assert.Contains("data-url=\"/openapi.json\"", first);

        Assert.Contains("<title>Internal</title>", second);
        Assert.Contains("data-url=\"/internal.json\"", second);
    }
}
