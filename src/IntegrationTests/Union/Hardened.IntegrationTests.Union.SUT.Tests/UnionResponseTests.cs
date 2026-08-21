namespace Hardened.IntegrationTests.Union.SUT.Tests;

/// <summary>
/// A C# 15 union serving real requests.
/// </summary>
/// <remarks>
/// <para>
/// The claim under test is that Hardened recognises a keyword-declared union through the same
/// structural check it uses for <c>Response&lt;T1..Tn&gt;</c>, and dispatches it identically.
/// UNION-RESPONSES-PLAN.md Part 9 experiment 2 asked exactly that and had never been answered
/// against a running application - only against generated text, which cannot show what a client
/// receives.
/// </para>
/// <para>
/// Deliberately the same assertions as the Responses fixture, against the same statuses. If the two
/// ever disagree, the structural match has stopped treating the keyword and the struct as one shape,
/// and that is a thing worth failing loudly rather than a thing to discover from a document.
/// </para>
/// </remarks>
public class UnionResponseTests {

    private record TodoBody(int Id, string Title);

    [HardenedTest]
    public async Task TheSuccessCaseSendsThePayloadRatherThanTheUnion(ITestWebApp app) {
        var response = await app.Get("/union/1");

        response.Assert.Ok();

        Assert.Equal(1, response.Deserialize<TodoBody>().Id);
    }

    [HardenedTest]
    public async Task AnErrorCaseAnswersItsOwnStatus(ITestWebApp app) {
        (await app.Get("/union/404")).Assert.NotFound();
    }

    [HardenedTest]
    public async Task CreatedAnswersTwoHundredAndOneWithItsLocation(ITestWebApp app) {
        var response = await app.Post(new { Title = "fresh" }, "/union");

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("/union/7", response.Headers["Location"].ToString());
    }

    [HardenedTest]
    public async Task ADeclaredConflictAnswersFourHundredAndNine(ITestWebApp app) {
        var response = await app.Post(new { Title = "taken" }, "/union");

        Assert.Equal(409, response.StatusCode);
    }

    [HardenedTest]
    public async Task NoContentAnswersTwoHundredAndFourWithAnEmptyBody(ITestWebApp app) {
        var response = await app.Delete("/union/1");

        Assert.Equal(204, response.StatusCode);
        Assert.Equal(string.Empty, await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task RemoveStillAnswersItsDeclaredNotFound(ITestWebApp app) {
        (await app.Delete("/union/404")).Assert.NotFound();
    }
}
