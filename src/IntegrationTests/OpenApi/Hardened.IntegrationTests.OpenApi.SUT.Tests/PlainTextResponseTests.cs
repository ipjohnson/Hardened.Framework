namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// A spec-first operation declared as <c>text/plain</c>, end to end.
/// </summary>
/// <remarks>
/// <para>
/// Every stage of this had to change for the route to exist at all. The parser filtered response
/// content down to media types containing "json" and dropped the rest, so this operation produced
/// no response schema; the model that carries it was not serialized between the build task and the
/// generator; and the builder never set <c>RawResponseContentType</c>. A spec could declare a
/// plain-text endpoint and the generator would emit one returning bare <c>Task</c>.
/// </para>
/// <para>
/// The interesting assertion is the body. A string handed to the JSON serializer comes back wrapped
/// in quotes with its newlines escaped, which is a valid JSON document and the wrong response.
/// </para>
/// </remarks>
public class PlainTextResponseTests {

    [HardenedTest]
    public async Task PlainTextOperation_SetsTheDeclaredContentType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/plain");

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
    }

    [HardenedTest]
    public async Task PlainTextOperation_WritesTheStringRatherThanJsonEncodingIt(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/plain");

        response.Assert.Ok();

        var body = await ReadBody(response);

        Assert.Equal("1: Buddy\n2: Luna", body);
        Assert.DoesNotContain("\\n", body);
        Assert.False(body.StartsWith('"'), "The body was JSON-encoded rather than written raw.");
    }

    private static async Task<string> ReadBody(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }
}
