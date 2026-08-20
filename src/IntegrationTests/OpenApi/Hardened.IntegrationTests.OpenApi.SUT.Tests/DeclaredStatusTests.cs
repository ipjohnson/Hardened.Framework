namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// What the document promises about a response, kept.
/// </summary>
/// <remarks>
/// <para>
/// Two promises, both of which the framework used to break silently. An operation declaring
/// <c>'201'</c> answered 200, because the status was parsed, carried through the serialized model
/// and read in one place - to write a doc comment. And a handler returning null answered 404 with an
/// empty body, whatever body the document declared for it.
/// </para>
/// <para>
/// Both matter for the same reason: a client generated from this document is written against what it
/// says, so a service that answers something else is wrong in a way no build catches.
/// </para>
/// </remarks>
public class DeclaredStatusTests {

    [HardenedTest]
    public async Task CreatePet_AnswersTheDeclaredCreatedStatus(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "{\"name\":\"Rex\"}", "/pets",
            request => request.Headers["Content-Type"] = "application/json");

        Assert.Equal(201, response.StatusCode);
    }

    /// <summary>An operation declaring nothing but 200 still answers 200.</summary>
    [HardenedTest]
    public async Task ListPets_AnswersTheUndeclaredDefault(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets");

        Assert.Equal(200, response.StatusCode);
    }

    /// <summary>
    /// A declared 204 answers 204, and writes no body.
    /// </summary>
    /// <remarks>
    /// The body suppression is the half that is easy to miss. Serializing into a 204 produces a
    /// response no conforming client reads the body of and some intermediaries reject outright.
    /// </remarks>
    [HardenedTest]
    public async Task DeletePet_AnswersTheDeclaredNoContentStatusWithNoBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Delete("/pets/1");

        Assert.Equal(204, response.StatusCode);

        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        Assert.Equal("", await reader.ReadToEndAsync());
    }

    [HardenedTest]
    public async Task GetPet_AnswersTheDeclaredNotFoundWhenTheHandlerReturnsNull(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/missing");

        Assert.Equal(404, response.StatusCode);
    }

    /// <summary>
    /// And carries the body the document declared for it, rather than nothing.
    /// </summary>
    /// <remarks>
    /// The status and its reason phrase are facts about the response - RFC 7807 defines
    /// <c>status</c> as the HTTP status code, so filling it is conformance. Nothing else is filled,
    /// because nothing else could be without inventing a domain value.
    /// </remarks>
    [HardenedTest]
    public async Task GetPet_CarriesTheDeclaredProblemBodyOnTheNotFound(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/missing");

        var problem = response.Deserialize<Problem>();

        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Not Found", problem.Title);
        Assert.Equal("about:blank", problem.Type);
    }

    /// <summary>
    /// And says nothing about why. A 404 body that explained itself would leak whether the resource
    /// exists, who owns it, or what rule refused it - a handler with something to say throws the
    /// generated exception type, which carries a body it wrote.
    /// </summary>
    [HardenedTest]
    public async Task GetPet_TheNotFoundBodySaysNothingAboutTheRequest(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/missing");

        var problem = response.Deserialize<Problem>();

        Assert.Null(problem!.Detail);
    }

    /// <summary>The same operation still answers normally for an id that resolves.</summary>
    [HardenedTest]
    public async Task GetPet_AnswersTheResourceWhenThereIsOne(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/7");

        response.Assert.Ok();

        Assert.Equal("7", response.Deserialize<Pet>()!.Id);
    }
}
