using Hardened.IntegrationTests.WebApp.SUT.Models;
using Microsoft.Extensions.Primitives;

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
    #region a token named after a keyword

    /// <summary>
    /// A route token named after a C# keyword. The path belongs to the contract, so the only way to
    /// declare its parameter is <c>@base</c> - and the generator compared that spelling, escape
    /// included, against the token <c>base</c>. It never matched, so the parameter went to the body
    /// and the build failed with HRDR005.
    /// </summary>
    [HardenedTest]
    public async Task APathTokenNamedAfterAKeywordBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/keyword/one/two");

        response.Assert.Ok();
        Assert.Equal("one:two", response.Deserialize<string>());
    }

    /// <summary>
    /// And the wire name is the token, not the escape - a caller sends <c>event</c>, and a
    /// validation error names <c>event</c>.
    /// </summary>
    [HardenedTest]
    public async Task AQueryParameterNamedAfterAKeywordBindsByItsUnescapedName(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/keyword-query?event=started");

        response.Assert.Ok();
        Assert.Equal("started", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task AQueryParameterNamedAfterAKeywordIsStillOptional(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/keyword-query");

        response.Assert.Ok();
        Assert.Equal("none", response.Deserialize<string>());
    }

    #endregion

    #region collections

    /// <summary>
    /// OpenAPI's default array style, <c>explode: true</c>: the key repeats. The query parser used
    /// to overwrite on a repeat, so this arrived as <c>GBP</c> alone and then failed to convert to
    /// a list at all.
    /// </summary>
    [HardenedTest]
    public async Task ARepeatedQueryKeyBindsAsAList(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-list?symbols=EUR&symbols=GBP&symbols=JPY");

        response.Assert.Ok();
        Assert.Equal("EUR|GBP|JPY", response.Deserialize<string>());
    }

    /// <summary>The same parameter written with <c>explode: false</c>.</summary>
    [HardenedTest]
    public async Task ACommaJoinedQueryValueBindsAsAList(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-list?symbols=EUR,GBP,JPY");

        response.Assert.Ok();
        Assert.Equal("EUR|GBP|JPY", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task OneValueBindsAsAListOfOne(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-list?symbols=EUR");

        response.Assert.Ok();
        Assert.Equal("EUR", response.Deserialize<string>());
    }

    /// <summary>
    /// Absent is null, not an empty list. A handler that has to tell "sent nothing" from "sent an
    /// empty list" can, which is the distinction ParseOptional draws for every other type.
    /// </summary>
    [HardenedTest]
    public async Task AnAbsentListParameterIsNull(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-list");

        response.Assert.Ok();
        Assert.Equal("none", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task EachItemConvertsToTheDeclaredItemType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-list-typed?ids=1&ids=2,3");

        response.Assert.Ok();
        Assert.Equal(6, response.Deserialize<int>());
    }

    /// <summary>An item that will not convert fails the request, as a scalar one does.</summary>
    [HardenedTest]
    public async Task AnItemThatWillNotConvertIsRejected(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-list-typed?ids=1,abc");

        response.Assert.BadRequest();
    }

    [HardenedTest]
    public async Task AnArrayParameterBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-array?tags=red&tags=green");

        response.Assert.Ok();
        Assert.Equal("red|green", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task AnAbsentArrayParameterIsNull(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-array");

        response.Assert.Ok();
        Assert.Equal("none", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task ARequiredListParameterIsRefusedWhenAbsent(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-list-required");

        response.Assert.BadRequest();
    }

    [HardenedTest]
    public async Task ARequiredListParameterBindsWhenPresent(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/query-list-required?symbols=EUR");

        response.Assert.Ok();
        Assert.Equal("EUR", response.Deserialize<string>());
    }

    /// <summary>
    /// A repeated header line, which is what a client sends when it does not join them itself.
    /// </summary>
    [HardenedTest]
    public async Task ARepeatedHeaderBindsAsAList(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/header-list",
            request => request.Headers["X-Tag"] = new StringValues(["red", "green"]));

        response.Assert.Ok();
        Assert.Equal("red|green", response.Deserialize<string>());
    }

    /// <summary>
    /// And the joined spelling, which RFC 9110 says a recipient may produce from the repeated one.
    /// </summary>
    [HardenedTest]
    public async Task ACommaJoinedHeaderBindsAsAList(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/header-list",
            request => request.Headers["X-Tag"] = "red, green");

        response.Assert.Ok();
        Assert.Equal("red|green", response.Deserialize<string>());
    }

    #endregion

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
