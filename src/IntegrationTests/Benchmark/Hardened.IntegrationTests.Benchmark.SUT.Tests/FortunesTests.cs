using Hardened.IntegrationTests.Benchmark.SUT.Tests.Support;

namespace Hardened.IntegrationTests.Benchmark.SUT.Tests;

/// <summary>
/// TechEmpower test type 4: fortunes, the HTML-rendering one.
/// </summary>
/// <remarks>
/// End to end this covers the whole template path: <c>x-hardened-template</c> in the spec reaches
/// the generated handler as an assignment to <c>Response.TemplateName</c>,
/// <c>TemplateResponseSerializer</c> claims the response ahead of JSON, and the RazorBlade engine
/// resolves the name to a compiled view and renders the model to the body.
/// </remarks>
public class FortunesTests {

    [HardenedTest]
    public async Task Fortunes_RendersHtmlRatherThanSerializingTheModel(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/fortunes");

        response.Assert.Ok();

        var body = await Body.Read(response);

        Assert.StartsWith("<!DOCTYPE html>", body);
        Assert.Contains("<title>Fortunes</title>", body);
        Assert.DoesNotContain("\"fortunes\":", body);
    }

    /// <summary>
    /// The content type comes from the template's base type - HtmlTemplate - rather than from the
    /// spec's media type or the file's extension.
    /// </summary>
    [HardenedTest]
    public async Task Fortunes_SetsTheHtmlContentType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/fortunes");

        Assert.Equal("text/html; charset=utf-8", response.Headers["Content-Type"]);
    }

    /// <summary>
    /// The reason the test type exists. The eleventh seeded row is a script tag, and it has to
    /// arrive as text - if it renders as markup the benchmark entry is an XSS hole.
    /// </summary>
    [HardenedTest]
    public async Task Fortunes_EscapesTheScriptRow(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/fortunes");

        var body = await Body.Read(response);

        Assert.Contains(
            "&lt;script&gt;alert(&quot;This should not be displayed in a browser alert box.&quot;);&lt;/script&gt;",
            body);
        Assert.DoesNotContain("<script>", body);
    }

    /// <summary>
    /// One row is added per request and the whole set is sorted by message, so the added row lands
    /// between other fortunes rather than at either end.
    /// </summary>
    [HardenedTest]
    public async Task Fortunes_AddsTheRequestTimeRowAndSortsByMessage(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/fortunes");

        var body = await Body.Read(response);

        Assert.Contains("Additional fortune added at request time.", body);

        var added = body.IndexOf("Additional fortune added at request time.", StringComparison.Ordinal);
        var afterEnough = body.IndexOf("After enough decimal places", StringComparison.Ordinal);
        var aBadRandom = body.IndexOf("A bad random number generator", StringComparison.Ordinal);

        Assert.True(aBadRandom < added, "Sorted by message, 'A bad random...' precedes the added row.");
        Assert.True(added < afterEnough, "Sorted by message, the added row precedes 'After enough...'.");
    }

    /// <summary>All thirteen rows are present: the twelve seeded plus the one added.</summary>
    [HardenedTest]
    public async Task Fortunes_RendersEveryRow(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/fortunes");

        var body = await Body.Read(response);

        Assert.Equal(13, CountOccurrences(body, "<tr><td>"));
    }

    /// <summary>Non-ASCII content survives the render and the UTF-8 write.</summary>
    [HardenedTest]
    public async Task Fortunes_PreservesNonAsciiContent(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/fortunes");

        Assert.Contains("フレームワークのベンチマーク", await Body.Read(response));
    }

    /// <summary>
    /// A byte order mark ahead of the doctype is what a StreamWriter built with the parameterless
    /// UTF8 encoding produces, and it is invisible in any assertion made on a decoded string.
    /// </summary>
    [HardenedTest]
    public async Task Fortunes_WritesNoByteOrderMark(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/fortunes");

        response.Body.Position = 0;

        Assert.Equal((byte)'<', response.Body.ReadByte());
    }

    private static int CountOccurrences(string haystack, string needle) {
        var count = 0;

        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) {
            count++;
        }

        return count;
    }
}
