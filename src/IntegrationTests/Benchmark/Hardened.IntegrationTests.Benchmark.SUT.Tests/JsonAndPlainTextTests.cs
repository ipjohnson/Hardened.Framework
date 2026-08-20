using Hardened.IntegrationTests.Benchmark.SUT.Tests.Support;
using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.Benchmark.SUT.Tests;

/// <summary>
/// TechEmpower test types 1 and 6: JSON serialization and plaintext.
/// </summary>
/// <remarks>
/// The pair is the point. Both operations are described in one spec and served by one pipeline, and
/// the only thing that makes them differ is the media type each declares its response under.
/// </remarks>
public class JsonAndPlainTextTests {

    /// <summary>
    /// The headers TechEmpower's own client sends, from
    /// <c>toolset/test_types/abstract_test_type.py</c>. Reproduced rather than simplified because
    /// the detail is the point: the json header lists <c>text/html;q=0.9</c>, so a route that also
    /// had a view would be contested, and the plaintext header lists <c>text/plain</c> first, which
    /// is the only reason /plaintext answers as text rather than as a quoted JSON string.
    /// </summary>
    private const string JsonAccept =
        "application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7";

    private const string PlainTextAccept =
        "text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7";

    private static Action<TestWebRequest> Accepting(string accept) =>
        request => request.Headers["Accept"] = new StringValues(accept);

    [HardenedTest]
    public async Task Json_ReturnsTheMessageObject(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/json", Accepting(JsonAccept));

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
        var response = await testWebApp.Get("/json", Accepting(JsonAccept));

        Assert.Equal("{\"message\":\"Hello, World!\"}", await Body.Read(response));
    }

    [HardenedTest]
    public async Task Json_SetsTheJsonContentType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/json", Accepting(JsonAccept));

        Assert.Equal("application/json", response.Headers["Content-Type"]);
    }

    /// <summary>
    /// The whole content-type chain in one assertion: no quotes, because the string went through
    /// the raw writer rather than the JSON serializer.
    /// </summary>
    [HardenedTest]
    public async Task PlainText_WritesTheBodyRaw(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/plaintext", Accepting(PlainTextAccept));

        response.Assert.Ok();

        Assert.Equal("Hello, World!", await Body.Read(response));
    }

    /// <summary>
    /// A client that expresses no preference gets the one representation this operation declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asserted the opposite until 2026-08-20 - that no Accept header falls back to JSON - on
    /// the grounds that answering text here would change what every handler returning a string does.
    /// That was a fair objection to the blunt fix and no longer applies to this one: only an
    /// operation that declares what it produces is negotiated against its own set, and a handler
    /// returning a bare string with nothing declared behaves exactly as it always did.
    /// </para>
    /// <para>
    /// <c>/plaintext</c> declares <c>text/plain</c> and nothing else. No Accept header means
    /// "whatever you have", and JSON is not something this operation has - it was answering with the
    /// declared string wrapped in quotes, which is a valid JSON document and the wrong response.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task PlainText_WithNoAcceptHeaderAnswersTheOneDeclaredType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/plaintext");

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
        Assert.Equal("Hello, World!", await Body.Read(response));
    }

    /// <summary>
    /// This document declares <c>x-hardened-content-negotiation: lenient</c>, so a client asking
    /// only for JSON at a text-only operation is served rather than refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The escape hatch, end to end: the root extension reaches the generated routing table, which
    /// registers the policy, which <c>SerializationLocatorService</c> reads. Under the default -
    /// strict - this same request is a 406, which <c>ContentNegotiationTests</c> in the OpenApi SUT
    /// asserts.
    /// </para>
    /// <para>
    /// One answer for the whole service, not per operation. A policy each operation restates is one
    /// that ends up applied unevenly, and the operation that quietly differs is invisible.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task PlainText_UnderLenientServesJsonRatherThanRefusing(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/plaintext", Accepting("application/json"));

        response.Assert.Ok();

        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal("\"Hello, World!\"", await Body.Read(response));
    }

    [HardenedTest]
    public async Task PlainText_SetsTheTextContentType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/plaintext", Accepting(PlainTextAccept));

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
    }
}
