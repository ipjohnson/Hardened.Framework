using System.Text.Json;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Constraints on a hand-written handler's own parameters, end to end: the 400 a query, header or
/// path value earns, the name it is reported under, and the facets the document publishes for it.
/// </summary>
/// <remarks>
/// The 0.19.0-rc1000 trial called this the single largest thing code-first gave up: a
/// <c>precision</c> bounded 2..8 was a hand-written check, and the document published an integer
/// with no bounds. Every handler behind these tests declares the constraint and nothing else.
/// </remarks>
public class ParameterConstraintTests {

    private static Action<TestWebRequest> Header(string name, string value) =>
        request => request.Headers[name] = new StringValues(value);

    private static RequestValidationError Refusal(TestWebResponse response) {
        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.NotNull(error);
        Assert.Equal("ValidationError", error.Type);

        return error;
    }

    // ---------------------------------------------------------------- query

    [HardenedTest]
    public async Task ABoundOnAQueryValueIsEnforced(ITestWebApp testWebApp) {
        var refused = Refusal(await testWebApp.Get("/constraints/precision?precision=9"));

        Assert.Contains(refused.Errors, e => e.Field == "precision" && e.Code == "range");

        var accepted = await testWebApp.Get("/constraints/precision?precision=4");

        accepted.Assert.Ok();

        Assert.Equal(4, accepted.Deserialize<int>());
    }

    // ---------------------------------------------------------------- header

    /// <summary>Pathed under the header's own name, which is what the caller sent.</summary>
    [HardenedTest]
    public async Task ALengthOnAHeaderIsPathedUnderTheHeaderName(ITestWebApp testWebApp) {
        var refused = Refusal(await testWebApp.Get("/constraints/region", Header("X-Region", "EUR")));

        Assert.Contains(refused.Errors, e => e.Field == "X-Region" && e.Code == "string_length");

        var accepted = await testWebApp.Get("/constraints/region", Header("X-Region", "EU"));

        accepted.Assert.Ok();

        Assert.Equal("EU", accepted.Deserialize<string>());
    }

    // ---------------------------------------------------------------- path

    /// <summary>
    /// The route constraint and the value constraint answer different questions. A token that is
    /// not an integer reaches no route, which is a 404; an integer outside the bound reaches the
    /// route and is refused, which is a 400.
    /// </summary>
    [HardenedTest]
    public async Task ABoundOnAPathTokenSitsBehindItsRouteConstraint(ITestWebApp testWebApp) {
        var noRoute = await testWebApp.Get("/constraints/page/abc");

        noRoute.Assert.NotFound();

        var refused = Refusal(await testWebApp.Get("/constraints/page/0"));

        Assert.Contains(refused.Errors, e => e.Field == "count" && e.Code == "range");

        var accepted = await testWebApp.Get("/constraints/page/7");

        accepted.Assert.Ok();

        Assert.Equal(7, accepted.Deserialize<int>());
    }

    // ---------------------------------------------------------------- required and pattern

    [HardenedTest]
    public async Task AQueryValueTheCallerMustSendIsReportedWhenAbsent(ITestWebApp testWebApp) {
        var absent = Refusal(await testWebApp.Get("/constraints/tagged"));

        Assert.Contains(absent.Errors, e => e.Field == "tag" && e.Code == "required");

        var wrongShape = Refusal(await testWebApp.Get("/constraints/tagged?tag=ABC"));

        Assert.Contains(wrongShape.Errors, e => e.Field == "tag" && e.Code == "pattern");

        var accepted = await testWebApp.Get("/constraints/tagged?tag=abc");

        accepted.Assert.Ok();

        Assert.Equal("abc", accepted.Deserialize<string>());
    }

    // ---------------------------------------------------------------- the document

    /// <summary>
    /// The other half of the same declaration: the document repeats the constraint as the facet it
    /// came from, so a generated client and a reader are told what the server enforces.
    /// </summary>
    [HardenedTest]
    public async Task TheDocumentPublishesTheConstraintsAsFacets(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.json");

        response.Assert.Ok();

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        var precision = Parameter(document.RootElement, "/constraints/precision", "precision");

        Assert.Equal(2, precision.GetProperty("schema").GetProperty("minimum").GetInt32());
        Assert.Equal(8, precision.GetProperty("schema").GetProperty("maximum").GetInt32());
        Assert.Equal("integer", precision.GetProperty("schema").GetProperty("type").GetString());

        var region = Parameter(document.RootElement, "/constraints/region", "X-Region");

        Assert.Equal(2, region.GetProperty("schema").GetProperty("minLength").GetInt32());
        Assert.Equal(2, region.GetProperty("schema").GetProperty("maxLength").GetInt32());

        var count = Parameter(document.RootElement, "/constraints/page/{count}", "count");

        Assert.Equal(1, count.GetProperty("schema").GetProperty("minimum").GetInt32());
        Assert.Equal(100, count.GetProperty("schema").GetProperty("maximum").GetInt32());

        var tag = Parameter(document.RootElement, "/constraints/tagged", "tag");

        Assert.True(tag.GetProperty("required").GetBoolean());
        Assert.Equal("^[a-z]+$", tag.GetProperty("schema").GetProperty("pattern").GetString());
    }

    private static JsonElement Parameter(JsonElement document, string path, string name) {
        foreach (var parameter in document
                     .GetProperty("paths").GetProperty(path).GetProperty("get")
                     .GetProperty("parameters").EnumerateArray()) {
            if (parameter.GetProperty("name").GetString() == name) {
                return parameter;
            }
        }

        throw new Xunit.Sdk.XunitException($"No parameter named '{name}' at '{path}'.");
    }
}
