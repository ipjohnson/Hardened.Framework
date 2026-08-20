using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// A response is negotiated against what the operation says it produces.
/// </summary>
/// <remarks>
/// <para>
/// It was negotiated against every registered serializer instead, and <c>MediaType.Matches</c>
/// answers true for <c>*/*</c> and for an absent <c>Accept</c> against any of them. So
/// <c>/pets/plain</c>, which declares <c>text/plain</c> and nothing else, was answered in JSON for
/// the header curl sends by default - the declared string wrapped in quotes with its newlines
/// escaped, which is a valid JSON document and the wrong response.
/// </para>
/// <para>
/// Three of the four cases below need no policy at all. Only the last is a decision, and it is one
/// answer for the whole service rather than something each operation restates.
/// </para>
/// </remarks>
public class ContentNegotiationTests {

    private static Action<TestWebRequest> Accepting(string accept) =>
        request => request.Headers["Accept"] = new StringValues(accept);

    private static async Task<string> Body(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// No <c>Accept</c> means "whatever you have", and what this operation has is text.
    /// </summary>
    [HardenedTest]
    public async Task NoAcceptHeaderAnswersTheFirstDeclaredType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/plain");

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
        Assert.Equal("1: Buddy\n2: Luna", await Body(response));
    }

    /// <summary>And <c>*/*</c> says the same thing explicitly.</summary>
    [HardenedTest]
    public async Task AnyMediaTypeAnswersTheFirstDeclaredType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/plain", Accepting("*/*"));

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
    }

    [HardenedTest]
    public async Task AnExplicitMatchIsHonoured(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/plain", Accepting("text/plain"));

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
    }

    /// <summary>
    /// A client listing several gets the one it can have, not a refusal.
    /// </summary>
    /// <remarks>
    /// The case that keeps this from being brittle: <c>Accept: application/json, text/plain;q=0.5</c>
    /// prefers JSON and will take text. It gets text, because that is the overlap - a 406 here would
    /// refuse a client that said outright it could read the answer.
    /// </remarks>
    [HardenedTest]
    public async Task AClientListingSeveralGetsTheOneOnOffer(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/pets/plain", Accepting("application/json, text/plain;q=0.5"));

        response.Assert.Ok();

        Assert.Equal("text/plain", response.Headers["Content-Type"]);
    }

    /// <summary>
    /// A client asking only for what this operation does not produce gets 406.
    /// </summary>
    /// <remarks>
    /// The one case that is a policy rather than plain correctness, and the service default is
    /// strict. 406 is the transport telling a client its <c>Accept</c> named nothing that exists -
    /// nothing about it is the document's to declare, which is why it is not derived from one.
    /// </remarks>
    [HardenedTest]
    public async Task AClientAskingOnlyForSomethingElseGets406(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/plain", Accepting("application/json"));

        Assert.Equal(406, response.StatusCode);
    }

    /// <summary>
    /// And the 406 says what the operation can produce, rather than being empty.
    /// </summary>
    /// <remarks>
    /// The client already knows what it asked for, so naming the alternatives tells it nothing it
    /// could not read in the document - and saves it a round trip through the document to find out.
    /// </remarks>
    [HardenedTest]
    public async Task The406NamesWhatTheOperationProduces(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/plain", Accepting("application/json"));

        var body = await Body(response);

        Assert.Contains("text/plain", body);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
    }

    /// <summary>
    /// An operation declaring JSON still answers JSON, which is most of them.
    /// </summary>
    [HardenedTest]
    public async Task AJsonOperationIsUnaffected(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets", Accepting("application/json"));

        response.Assert.Ok();

        Assert.Equal("application/json", response.Headers["Content-Type"]);
    }

    /// <summary>
    /// And so does one asked with no preference at all.
    /// </summary>
    [HardenedTest]
    public async Task AJsonOperationWithNoAcceptHeaderIsUnaffected(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets");

        response.Assert.Ok();

        Assert.Equal("application/json", response.Headers["Content-Type"]);
    }
}
