using System.Text.Json;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// A 404 the contract declares as <c>application/problem+json</c>, through the generated
/// application.
/// </summary>
/// <remarks>
/// The 0.41 trial's B-01 and B-02, on <c>/pets/{petId}/checkup</c> in <c>petstore.yaml</c>. The GET
/// declares JSON for its 200 and a problem for its 404, which went out as <c>application/json</c>.
/// The DELETE declares the problem and nothing else, which answered 500 with no body.
/// </remarks>
public class ProblemJsonTests
{
    private static Action<TestWebRequest> Accepting(string accept) =>
        request => request.Headers[KnownHeaders.Accept] = new StringValues(accept);

    [ModuleTest]
    [InlineData("*/*")]
    [InlineData("application/json")]
    [InlineData("application/problem+json")]
    public async Task AFailureDeclaredAsAProblemBesideAJsonSuccessIsOne(
        string accept,
        ITestWebApp app
    )
    {
        var response = await app.Get("/pets/missing/checkup", Accepting(accept));

        Assert.Equal(404, response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            response.Headers[KnownHeaders.ContentType].ToString()
        );

        using var body = JsonDocument.Parse(await response.ReadTextAsync());

        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
    }

    [ModuleTest]
    public async Task TheSuccessBesideItIsJson(ITestWebApp app)
    {
        var response = await app.Get("/pets/1/checkup", Accepting("*/*"));

        Assert.Equal(200, response.StatusCode);
        Assert.StartsWith(
            "application/json",
            response.Headers[KnownHeaders.ContentType].ToString()
        );
    }

    /// <summary>
    /// The 0.42 trial's B-19: the 406 named application/problem+json as something the GET
    /// produces.
    /// </summary>
    [ModuleTest]
    public async Task The406NamesTheSuccessAlone(ITestWebApp app)
    {
        var response = await app.Get("/pets/1/checkup", Accepting("application/xml"));

        Assert.Equal(406, response.StatusCode);

        using var body = JsonDocument.Parse(await response.ReadTextAsync());

        Assert.Equal(
            "This operation produces application/json.",
            body.RootElement.GetProperty("detail").GetString()
        );
    }

    [ModuleTest]
    public async Task AFailureDeclaredOnlyAsAProblemIsOne(ITestWebApp app)
    {
        var response = await app.Delete("/pets/missing/checkup");

        Assert.Equal(404, response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            response.Headers[KnownHeaders.ContentType].ToString()
        );
        Assert.Contains("\"status\":404", await response.ReadTextAsync());
    }

    [ModuleTest]
    public async Task TheDocumentDeclaresTheProblem(ITestWebApp app)
    {
        using var served = JsonDocument.Parse(
            await (await app.Get("/openapi.json")).ReadTextAsync()
        );

        var notFound = served
            .RootElement.GetProperty("paths")
            .GetProperty("/pets/{petId}/checkup")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("404")
            .GetProperty("content");

        Assert.Equal(
            ["application/problem+json"],
            notFound.EnumerateObject().Select(mediaType => mediaType.Name)
        );
    }
}
