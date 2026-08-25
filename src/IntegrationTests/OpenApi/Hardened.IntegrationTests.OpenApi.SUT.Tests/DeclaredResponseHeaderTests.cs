namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// A header the description declares, arriving on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="DeclaredStatusTests"/>, and the same class of promise: the document
/// said <c>201</c> carries a <c>Location</c>, and nothing sent one. It broke in three independent
/// places at once - <c>OpenApiSpecParser</c> never read <c>responses.*.headers</c>, the response
/// models had no field to hold it, and <c>RequestModelBuilder.BuildUnionCases</c> answered
/// <c>appliesHeaders</c> with the literal <c>false</c>. Any one of them alone was enough, which is
/// why fixing this needed all three and why a test at this level is the one that could not have
/// passed for the wrong reason.
/// </para>
/// <para>
/// It is asserted here rather than only in the generator tests because the generated text being
/// right and the header reaching a client are different claims. The switch arm calls
/// <c>ApplyHeaders</c> before serialization; a filter ordered past that point would compile, read
/// correctly, and still send nothing.
/// </para>
/// </remarks>
public class DeclaredResponseHeaderTests {

    [HardenedTest]
    public async Task CreatePet_SendsTheLocationTheDescriptionDeclares(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "{\"name\":\"Rex\"}", "/pets",
            request => request.Headers["Content-Type"] = "application/json");

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("/pets/3", response.Headers["Location"].ToString());
    }

    /// <summary>
    /// The header goes out with the response that declares it, and with no other.
    /// </summary>
    /// <remarks>
    /// <c>Pet</c> is the payload of this 201 and of <c>GET /pets/{petId}</c> both, so a fix that
    /// put the header on the payload type would pass the test above and send a <c>Location</c> on
    /// every read. That is the reason a success declaring a header is wrapped and one declaring
    /// none is left as the bare payload it always was.
    /// </remarks>
    [HardenedTest]
    public async Task GetPet_SendsNoLocation(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/1");

        Assert.Equal(200, response.StatusCode);
        Assert.False(response.Headers.ContainsKey("Location"));
    }
}
