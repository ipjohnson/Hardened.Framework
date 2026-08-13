using Hardened.IntegrationTests.Benchmark.SUT.Tests.Support;

namespace Hardened.IntegrationTests.Benchmark.SUT.Tests;

/// <summary>
/// TechEmpower test types 1 and 6: JSON serialization and plaintext.
/// </summary>
/// <remarks>
/// The pair is the point. Both operations are described in one spec and served by one pipeline, and
/// the only thing that makes them differ is the media type each declares its response under.
/// </remarks>
public class JsonAndPlainTextTests {

    [HardenedTest]
    public async Task Json_ReturnsTheMessageObject(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/json");

        response.Assert.Ok();

        var message = response.Deserialize<HelloMessage>();

        Assert.NotNull(message);
        Assert.Equal("Hello, World!", message.Message);
    }

    /// <summary>
    /// The benchmark specifies the body exactly, and camelCase is what makes it match.
    /// </summary>
    [HardenedTest]
    public async Task Json_SerializesTheBodyTheBenchmarkSpecifies(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/json");

        Assert.Equal("{\"message\":\"Hello, World!\"}", await Body.Read(response));
    }

    [HardenedTest]
    public async Task Json_SetsTheJsonContentType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/json");

        Assert.Equal("application/json", response.Headers["Content-Type"]);
    }

    /// <summary>
    /// The whole content-type chain in one assertion: no quotes, because the string went through
    /// the raw writer rather than the JSON serializer.
    /// </summary>
    [HardenedTest]
    public async Task PlainText_WritesTheBodyRaw(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/plaintext");

        response.Assert.Ok();

        Assert.Equal("Hello, World!", await Body.Read(response));
    }

    [HardenedTest]
    public async Task PlainText_SetsTheTextContentType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/plaintext");

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
    }
}
