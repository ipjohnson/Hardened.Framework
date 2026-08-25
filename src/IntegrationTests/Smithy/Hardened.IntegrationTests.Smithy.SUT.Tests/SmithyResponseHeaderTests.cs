namespace Hardened.IntegrationTests.Smithy.SUT.Tests;

/// <summary>
/// <c>@httpHeader</c> on an output member, on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The Smithy half of the same defect <c>DeclaredResponseHeaderTests</c> covers for OpenAPI, and it
/// failed one step earlier: the OpenAPI parser never read <c>responses.*.headers</c>, while this one
/// read the member and put it in the body. A model declaring <c>@httpHeader("Location")</c> sent
/// <c>{"location": "..."}</c> as a JSON property and no header - which is worse than dropping it,
/// because the value is on the wire in a place no client is looking.
/// </para>
/// <para>
/// Silent because <c>smithy.api#httpHeader</c> sits in <c>SmithyTraits.Mapped</c>, the set that
/// suppresses the unhandled-trait report. It was classified as handled and was not handled.
/// </para>
/// </remarks>
public class SmithyResponseHeaderTests {

    [HardenedTest]
    public async Task CreatePet_SendsTheHeaderTheModelBinds(ITestWebApp app) {
        var response = await app.Post(new { name = "Whiskers", kind = "cat" }, "/pets");

        response.Assert.Ok();

        Assert.Equal("/pets/3", response.Headers["Location"].ToString());
    }

    /// <summary>
    /// And does not also send it in the body.
    /// </summary>
    /// <remarks>
    /// Binding the member out of the body is half the fix, and the half a partial one would skip:
    /// collecting the header while leaving the member in the schema would send the value twice and
    /// pass the assertion above.
    /// </remarks>
    [HardenedTest]
    public async Task CreatePet_DoesNotAlsoSendTheHeaderInTheBody(ITestWebApp app) {
        var response = await app.Post(new { name = "Whiskers", kind = "cat" }, "/pets");

        response.Assert.Ok();

        var text = await response.ReadTextAsync();

        Assert.Contains("Whiskers", text);
        Assert.DoesNotContain("location", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An operation binding no header is untouched.
    /// </summary>
    [HardenedTest]
    public async Task GetPet_SendsNoLocation(ITestWebApp app) {
        var response = await app.Get("/pets/1");

        response.Assert.Ok();

        Assert.False(response.Headers.ContainsKey("Location"));
    }
}
