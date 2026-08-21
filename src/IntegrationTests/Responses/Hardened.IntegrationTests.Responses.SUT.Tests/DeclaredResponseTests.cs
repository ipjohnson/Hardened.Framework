namespace Hardened.IntegrationTests.Responses.SUT.Tests;

/// <summary>
/// What a declared response set actually puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The gap these close: every other test of this feature asserts a model or generated text, and a
/// dispatch can satisfy all of them while sending the wrong thing. The specification-first path did
/// exactly that - it recognised the set, assigned the container, and answered
/// <c>{"value":{"id":1}}</c> at the operation's default status. Only a request shows it.
/// </para>
/// <para>
/// Asserted on the wire rather than on the handler's return value, for the same reason: the point is
/// what a client receives, and the wrapper is invisible from inside the application.
/// </para>
/// </remarks>
public class DeclaredResponseTests {

    private record TodoBody(int Id, string Title);

    private record ApiErrorBody(string Code, string Message);

    [HardenedTest]
    public async Task TheSuccessCaseSendsThePayloadRatherThanTheContainer(ITestWebApp app) {
        var response = await app.Get("/responses/1");

        response.Assert.Ok();

        // The container has a public Value; if it were serialised this would be 0.
        Assert.Equal(1, response.Deserialize<TodoBody>().Id);
    }

    [HardenedTest]
    public async Task AnErrorCaseAnswersItsOwnStatus(ITestWebApp app) {
        (await app.Get("/responses/404")).Assert.NotFound();
    }

    /// <summary>
    /// 201 from the case, not 200 from the handler having returned successfully.
    /// </summary>
    [HardenedTest]
    public async Task CreatedAnswersTwoHundredAndOneWithItsLocation(ITestWebApp app) {
        var response = await app.Post(new { Title = "fresh" }, "/responses");

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("/responses/7", response.Headers["Location"].ToString());
    }

    /// <summary>
    /// Created&lt;T&gt; carries the payload; sending the wrapper would nest it under Value and ship
    /// the Location beside it.
    /// </summary>
    [HardenedTest]
    public async Task CreatedSendsTheBodyItCarries(ITestWebApp app) {
        var response = await app.Post(new { Title = "fresh" }, "/responses");

        Assert.Equal("fresh", response.Deserialize<TodoBody>().Title);
    }

    [HardenedTest]
    public async Task ADeclaredConflictAnswersFourHundredAndNine(ITestWebApp app) {
        var response = await app.Post(new { Title = "taken" }, "/responses");

        Assert.Equal(409, response.StatusCode);
    }

    /// <summary>
    /// 204, and nothing in the body.
    /// </summary>
    /// <remarks>
    /// The bodyless case is the one a response set had no way to express at all until the success
    /// branch existed, and the one whose failure mode is a 200 carrying "null".
    /// </remarks>
    [HardenedTest]
    public async Task NoContentAnswersTwoHundredAndFourWithAnEmptyBody(ITestWebApp app) {
        var response = await app.Delete("/responses/1");

        Assert.Equal(204, response.StatusCode);
        Assert.Equal(string.Empty, await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task RemoveStillAnswersItsDeclaredNotFound(ITestWebApp app) {
        (await app.Delete("/responses/404")).Assert.NotFound();
    }

    /// <summary>
    /// A typed error body puts the T on the wire, not the wrapper that named the status.
    /// </summary>
    [HardenedTest]
    public async Task ATypedErrorSendsItsBodyRatherThanTheWrapper(ITestWebApp app) {
        var response = await app.Get("/responses/typed/404");

        response.Assert.NotFound();
        Assert.Equal("not_found", response.Deserialize<ApiErrorBody>().Code);
    }
}
