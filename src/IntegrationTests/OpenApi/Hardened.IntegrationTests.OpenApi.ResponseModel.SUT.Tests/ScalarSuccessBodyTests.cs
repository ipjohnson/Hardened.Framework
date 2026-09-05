using Microsoft.Extensions.Primitives;

using Hardened.Requests.Abstract.Responses;

namespace Hardened.IntegrationTests.OpenApi.ResponseModel.SUT.Tests;

/// <summary>
/// A scalar success in a declared response set, end to end.
/// </summary>
/// <remarks>
/// <para>
/// The dispatch decided a case's body by asking whether the contract named a schema, so a
/// <c>text/plain</c> success - <c>type: string</c>, no <c>$ref</c> - was built the same bodyless
/// case a 204 gets, and the switch set <c>ShouldSerialize</c> false on the one response the
/// operation exists to answer. The 200 was the declared status with an empty body, in every
/// front end that reached the response-set path.
/// </para>
/// <para>
/// The sibling SUT could not catch this: it runs the Throws model, its declared errors are
/// thrown, and <c>RequiresResponseSet</c> stays false - the broken branch never executed there.
/// </para>
/// </remarks>
public class ScalarSuccessBodyTests {

    [HardenedTest]
    public async Task AScalarSuccessCarriesItsBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/labels/7");

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
        Assert.Equal("Label 7", await Body(response));
    }

    /// <summary>
    /// The returned error case, on the same operation. This is the parser half of the fix
    /// travelling the returned path: the error representation is in the declared set, so the JSON
    /// body resolves a serializer on an operation whose success is text.
    /// </summary>
    [HardenedTest]
    public async Task AReturnedNotFoundCaseAnswersAsJson(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/labels/missing");

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);

        var problem = response.Deserialize<Problem>();

        // The handler returned the framework's NotFound; the conversion the build wrote filled the
        // contract's Problem from it, title and status from the record and the detail from the
        // handler.
        Assert.Equal("Not Found", problem!.Title);
        Assert.Equal(404, problem.Status);
        Assert.Equal("No such label", problem.Detail);
    }

    /// <summary>
    /// A client asking for the text specifically still gets it, so the error entry appended to
    /// the declared set did not take the success over.
    /// </summary>
    [HardenedTest]
    public async Task AskingForTextStillAnswersText(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/labels/7", request => request.Headers["Accept"] = new StringValues("text/plain"));

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
    }

    /// <summary>
    /// The bodyless case stays bodyless. hasBody keyed on more than the schema must not start
    /// serializing into a 204.
    /// </summary>
    [HardenedTest]
    public async Task ANoContentCaseStillSerializesNothing(ITestWebApp testWebApp) {
        var response = await testWebApp.Post("", "/labels/7/archive");

        Assert.Equal(204, response.StatusCode);
        Assert.Equal("", await Body(response));
    }

    [HardenedTest]
    public async Task ANoContentOperationStillAnswersItsReturnedNotFound(ITestWebApp testWebApp) {
        var response = await testWebApp.Post("", "/labels/missing/archive");

        Assert.Equal(404, response.StatusCode);

        var problem = response.Deserialize<Problem>();

        // NotFound.Default, converted to the cached case: the generic detail, and the status.
        Assert.Equal(404, problem!.Status);
        Assert.Equal(NotFound.Default.Detail, problem.Detail);
    }

    private static async Task<string> Body(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }
}
