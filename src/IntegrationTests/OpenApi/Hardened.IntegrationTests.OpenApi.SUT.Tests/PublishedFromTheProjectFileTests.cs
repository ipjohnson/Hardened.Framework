namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// This application registers nothing. Its document is served and its reference page exists because
/// the item that declares the spec says so:
///
/// <code>
/// &lt;HardenedOpenApiSpec Include="Specs\petstore.yaml"&gt;
///     &lt;PublishUrl&gt;/openapi.yaml&lt;/PublishUrl&gt;
///     &lt;UiUrl&gt;/docs&lt;/UiUrl&gt;
/// &lt;/HardenedOpenApiSpec&gt;
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A specification-first application's contract is a build input, so where it publishes that
/// contract is a fact about the file. Stating it on an attribute instead means naming the file in
/// two places with nothing checking they agree - and with several specs, once per spec.
/// </para>
/// <para>
/// The page goes through the same <c>HardenedOpenApiUi</c> module an attribute-routed application
/// applies as an attribute. One implementation reached two ways, rather than a second one for the
/// specification-first direction that would drift from it.
/// </para>
/// </remarks>
public class PublishedFromTheProjectFileTests {

    [HardenedTest]
    public async Task TheDocumentIsServedWherePublishUrlSaid(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.yaml");

        response.Assert.Ok();

        Assert.Equal("application/yaml", response.Headers["Content-Type"].ToString());
        Assert.Contains("openapi:", await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task TheReferencePageIsServedWhereUiUrlSaid(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/docs");

        response.Assert.Ok();

        Assert.Equal("text/html; charset=utf-8", response.Headers["Content-Type"].ToString());
    }

    /// <summary>
    /// And it points at the document this application actually serves, which is the correspondence
    /// the metadata exists to keep - both URLs come from the same item, so they cannot disagree.
    /// </summary>
    /// <remarks>
    /// <c>PublishUrl</c>, which is the generated document, rather than <c>SourceUrl</c>. The page
    /// renders operations, and the source is whatever the author wrote - for a Smithy model that is
    /// an AST, which is what the page used to be handed and why it rendered nothing.
    /// </remarks>
    [HardenedTest]
    public async Task ThePageReadsTheDocumentThatWasPublished(ITestWebApp testWebApp) {
        var page = await (await testWebApp.Get("/docs")).ReadTextAsync();

        Assert.Contains("data-url=\"/openapi.json\"", page);

        var document = await testWebApp.Get("/openapi.json");

        document.Assert.Ok();
    }

    /// <summary>
    /// Nothing was published anywhere else. A page or a document at a path nobody asked for would
    /// mean the defaults were leaking through rather than the metadata being read.
    /// </summary>
    /// <remarks>
    /// This item names <c>/openapi.json</c>, <c>/openapi.yaml</c> and <c>/docs</c>, so the paths
    /// worth asserting on are the conventional ones it did not name.
    /// </remarks>
    [HardenedTest]
    public async Task NothingIsServedAtTheDefaultPaths(ITestWebApp testWebApp) {
        Assert.Equal(404, (await testWebApp.Get("/openapi")).StatusCode);
        Assert.Equal(404, (await testWebApp.Get("/swagger.json")).StatusCode);
        Assert.Equal(404, (await testWebApp.Get("/swagger")).StatusCode);
    }
}
