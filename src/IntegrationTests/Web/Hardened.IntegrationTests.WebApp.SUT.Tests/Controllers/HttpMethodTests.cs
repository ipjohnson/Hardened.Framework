namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// DELETE and PATCH, end to end through routing, binding and serialization.
///
/// Both verbs were listed in the web generator's attribute set from the first commit, but
/// <c>DeleteAttribute</c> and <c>PatchAttribute</c> shipped as empty internal classes that did not
/// derive from Attribute - so the README documented two route attributes no project could apply.
/// These tests fail to compile if that regresses, and fail to pass if the verb stops routing.
/// </summary>
public class HttpMethodTests {

    [HardenedTest]
    public async Task DeleteBindsAPathToken(ITestWebApp testWebApp) {
        var response = await testWebApp.Delete("/verbs/item/abc123");

        response.Assert.Ok();
        Assert.Equal("deleted:abc123", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task DeleteBindsAQueryString(ITestWebApp testWebApp) {
        var response = await testWebApp.Delete("/verbs/item?name=widget");

        response.Assert.Ok();
        Assert.Equal("deleted:widget", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task PatchBindsABodyAndAPathToken(ITestWebApp testWebApp) {
        var model = new MathAddModel { Values = new List<int> { 1, 2, 3 } };

        var response = await testWebApp.Patch(model, "/verbs/item/totals");

        response.Assert.Ok();
        Assert.Equal("patched:totals:1,2,3", response.Deserialize<string>());
    }

    /// <summary>
    /// The same path under a different verb has to reach a different handler. Routing on path alone
    /// would make whichever registered first answer both.
    /// </summary>
    [HardenedTest]
    public async Task VerbsOnTheSamePathStayApart(ITestWebApp testWebApp) {
        var get = await testWebApp.Get("/verbs/item/abc123");
        var delete = await testWebApp.Delete("/verbs/item/abc123");

        get.Assert.Ok();
        delete.Assert.Ok();

        Assert.Equal("got:abc123", get.Deserialize<string>());
        Assert.Equal("deleted:abc123", delete.Deserialize<string>());
    }

    /// <summary>
    /// A hand-written handler answers the status its attribute declares.
    /// </summary>
    /// <remarks>
    /// The attribute-routed half of the same wiring the specification-first routes exercise. Both
    /// land on <c>ResponseInformationModel.DefaultStatusCode</c>, and this is here so a change that
    /// wires only the described side cannot pass.
    /// </remarks>
    [HardenedTest]
    public async Task ADeclaredSuccessStatusIsAnswered(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(null!, "/verbs/created");

        Assert.Equal(201, response.StatusCode);
    }

    /// <summary>
    /// A declared 204 answers 204 and writes nothing, whatever the handler returned.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredNoContentStatusWritesNoBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Delete("/verbs/emptied");

        Assert.Equal(204, response.StatusCode);

        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        Assert.Equal("", await reader.ReadToEndAsync());
    }

    /// <summary>A handler declaring nothing still answers 200.</summary>
    [HardenedTest]
    public async Task AHandlerDeclaringNoStatusStillAnswers200(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/verbs/item/abc123");

        Assert.Equal(200, response.StatusCode);
    }
}
