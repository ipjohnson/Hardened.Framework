using System.IO.Compression;
using System.Text.Json;

namespace Hardened.IntegrationTests.OpenApi.ResponseModel.SUT.Tests;

/// <summary>
/// A contract that declares its validation status is answered at it.
/// </summary>
/// <remarks>
/// Arm C of the second trial declared <c>422</c> for its validation error; the build published
/// it and the service answered the stock 400, because the declared status was wired to nothing.
/// <c>/labels</c> POST is the same declaration here: its validation failures answer 422, the
/// operations declaring nothing keep their 400, and the published document carries the declared
/// 422 without a synthesized 400 beside it.
/// </remarks>
public class DeclaredValidationStatusTests {

    [HardenedTest]
    public async Task AValidationFailureAnswersTheDeclaredStatus(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            """{"name":""}""", "/labels",
            request => request.Headers["Content-Type"] = "application/json");

        Assert.Equal(422, response.StatusCode);

        var error = response.Deserialize<ValidationShape>();

        Assert.Equal("ValidationError", error!.Type);
        Assert.Contains(error.Errors, e => e.Field.Contains("name"));
    }

    [HardenedTest]
    public async Task AValidRequestStillAnswersItsSuccess(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            """{"name":"aisle four"}""", "/labels",
            request => request.Headers["Content-Type"] = "application/json");

        response.Assert.Ok();
    }

    /// <summary>
    /// The document carries the declared 422 and no synthesized 400 beside it: validation no
    /// longer produces a 400 on this operation, so publishing one would be the next untruth.
    /// </summary>
    [HardenedTest]
    public async Task TheDocumentCarriesTheDeclaredStatusAlone(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();
        response.Body.Position = 0;

        await using var gzip = new GZipStream(response.Body, CompressionMode.Decompress);

        var document = JsonDocument.Parse(await new StreamReader(gzip).ReadToEndAsync()).RootElement;

        var responses = document.GetProperty("paths").GetProperty("/labels")
            .GetProperty("post").GetProperty("responses");

        Assert.True(responses.TryGetProperty("422", out _));
        Assert.False(responses.TryGetProperty("400", out _));
    }

    private record ValidationShape(string Type, string Message, List<FieldShape> Errors);

    private record FieldShape(string Field, string Code, string Message);
}
