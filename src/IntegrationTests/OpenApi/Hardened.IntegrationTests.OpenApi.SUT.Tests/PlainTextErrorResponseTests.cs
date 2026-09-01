namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// Errors on an operation whose success is not JSON.
/// </summary>
/// <remarks>
/// <para>
/// The declared set was collected from the 2xx responses alone, so
/// <c>/pets/{petId}/label</c> declared <c>text/plain</c> and nothing else - a set no error model
/// can travel as. Its declared 404 and the framework's own binding 400 both failed to find a
/// serializer, and the locator's refusal reached the caller as an empty 500. The error
/// representations are in the set now, after the success ones, so an error is negotiated like an
/// error while a client with no preference still gets the text the document leads with.
/// </para>
/// </remarks>
public class PlainTextErrorResponseTests {

    /// <summary>
    /// The ordering half of the fix: the success representation still leads the declared set, so
    /// a client with no preference gets the text, not the JSON the errors added.
    /// </summary>
    [HardenedTest]
    public async Task TheSuccessStillLeadsTheDeclaredSet(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/7/label");

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
        Assert.Equal("Pet 7", await Body(response));
    }

    [HardenedTest]
    public async Task TheDeclaredNotFoundAnswersAsJson(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/missing/label");

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);

        var problem = response.Deserialize<Problem>();

        Assert.NotNull(problem);
        Assert.Equal(404, problem!.Status);
        Assert.Equal("Not Found", problem.Title);
    }

    /// <summary>
    /// The framework's own refusal takes the same route as a declared error: a query value that
    /// does not parse answers the standard 400 envelope, in JSON, on an operation that produces
    /// text.
    /// </summary>
    [HardenedTest]
    public async Task ABindingFailureAnswersTheValidationEnvelopeAsJson(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/7/label?copies=abc");

        response.Assert.BadRequest();

        Assert.Equal("application/json", response.Headers["Content-Type"]);

        var error = response.Deserialize<ValidationShape>();

        Assert.Equal("ValidationError", error!.Type);
        Assert.Equal("copies", Assert.Single(error.Errors).Field);
    }

    private record ValidationShape(string Type, string Message, List<FieldShape> Errors);

    private record FieldShape(string Field, string Code, string Message);

    private static async Task<string> Body(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }
}
