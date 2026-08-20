using Hardened.IntegrationTests.WebApp.SUT.Models;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Every parameter binding source, driven through routing and the generated binding code.
///
/// Writing these surfaced three source generator defects that no existing test reached:
/// named binding attributes emitted their name double-quoted, handlers with metadata but no
/// parameters put the metadata array in the parameters slot, and [FromHeader] called Get on
/// a plain dictionary. All three produced code that did not compile, so any project using
/// those features failed to build.
/// </summary>
public class ParameterBindingTests {

    [HardenedTest]
    public async Task PathTokenBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path/abc123");

        response.Assert.Ok();
        Assert.Equal("abc123", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task MultiplePathTokensBindInOrder(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/pair/first/second");

        response.Assert.Ok();
        Assert.Equal("first:second", response.Deserialize<string>());
    }

    /// <summary>Path tokens arrive as strings and are converted to the declared type.</summary>
    [HardenedTest]
    public async Task PathTokenConvertsToDeclaredType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path-typed/21");

        response.Assert.Ok();
        Assert.Equal(42, response.Deserialize<int>());
    }

    [HardenedTest]
    public async Task QueryStringBindsByParameterName(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query?name=hardened");

        response.Assert.Ok();
        Assert.Equal("hardened", response.Deserialize<string>());
    }

    /// <summary>
    /// The named form. This is what emitted a double-quoted literal before the generator fix.
    /// </summary>
    [HardenedTest]
    public async Task QueryStringBindsByAttributeName(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-named?q=searchterm");

        response.Assert.Ok();
        Assert.Equal("searchterm", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task QueryStringConvertsToDeclaredType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-typed?page=7");

        response.Assert.Ok();
        Assert.Equal(8, response.Deserialize<int>());
    }

    /// <summary>
    /// A percent-encoded value arrives decoded, the same as it does on Kestrel.
    /// </summary>
    /// <remarks>
    /// The harness built its query collection with a parser of its own that stored the raw
    /// substring, so this value bound as <c>2026-09-10T09%3A00%3A00%2B00%3A00</c> and any handler
    /// parsing it answered 400 - while the identical request over a socket answered 200. Both hosts
    /// read <c>QueryStringParser</c> now, which is what keeps them from drifting again.
    /// </remarks>
    [HardenedTest]
    public async Task QueryStringArrivesPercentDecoded(ITestWebApp testWebApp) {
        var encoded = Uri.EscapeDataString("2026-09-10T09:00:00+00:00");

        var response = await testWebApp.Get("/binding/query?name=" + encoded);

        response.Assert.Ok();
        Assert.Equal("2026-09-10T09:00:00+00:00", response.Deserialize<string>());
    }

    /// <summary>
    /// Base64 pads with <c>'='</c>, which the harness's own parser dropped the whole pair over.
    /// </summary>
    [HardenedTest]
    public async Task QueryStringKeepsAValueContainingAnEqualsSign(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query?name=YWJjZA==");

        response.Assert.Ok();
        Assert.Equal("YWJjZA==", response.Deserialize<string>());
    }

    /// <summary>
    /// [FromHeader] binding. Documented since the framework's first release and, until the
    /// generator fix, incapable of compiling.
    /// </summary>
    [HardenedTest]
    public async Task HeaderBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/header",
            request => request.Headers["X-Tenant"] = "acme");

        response.Assert.Ok();
        Assert.Equal("acme", response.Deserialize<string>());
    }

    /// <summary>
    /// Path token, query string, header and an injected service in one handler - the case
    /// most likely to break if binding order or parameter indexing regresses.
    /// </summary>
    /// <summary>
    /// A cookie, which had no binding attribute at all before <c>[FromCookie]</c> — the only way
    /// to read one was to take <c>IExecutionRequest</c> and parse the raw strings by hand.
    /// </summary>
    [HardenedTest]
    public async Task CookieBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/cookie",
            request => request.Headers["Cookie"] = "session=abc123");

        response.Assert.Ok();
        Assert.Equal("abc123", response.Deserialize<string>());
    }

    /// <summary>The attribute's name wins over the parameter's, as it does for header and query.</summary>
    [HardenedTest]
    public async Task CookieBindsByAttributeName(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/cookie-named",
            request => request.Headers["Cookie"] = "session=abc123; theme=dark");

        response.Assert.Ok();
        Assert.Equal("dark", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task AllBindingSourcesCombineInASingleHandler(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/mixed/id-9?filter=active",
            request => request.Headers["X-Tenant"] = "acme");

        response.Assert.Ok();
        Assert.Equal("id-9|active|acme|3", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task BodyAndPathTokenCoexist(ITestWebApp testWebApp) {
        var model = new MathAddModel { Values = new List<int> { 1, 2, 3 } };

        var response = await testWebApp.Post(model, "/binding/body/totals");

        response.Assert.Ok();
        Assert.Equal("totals:1,2,3", response.Deserialize<string>());
    }
}
