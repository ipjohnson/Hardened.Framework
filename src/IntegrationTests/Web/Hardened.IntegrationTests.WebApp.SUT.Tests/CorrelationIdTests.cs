namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The correlation id, end to end through a real application.
/// </summary>
/// <remarks>
/// Nothing in this application asks for any of it: the id is issued by the context, put in scope by
/// the request logger and returned by a filter the middleware service seeds itself. So what is under
/// test is largely that none of those need wiring up per host.
/// </remarks>
public class CorrelationIdTests {

    private const string Header = "X-Correlation-Id";

    [HardenedTest]
    public async Task EveryResponseCarriesACorrelationId(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("GET", null, "/binding/path/42");

        Assert.False(string.IsNullOrEmpty(response.Headers[Header].ToString()));
    }

    /// <summary>Shaped like a trace id whether or not one was in play.</summary>
    [HardenedTest]
    public async Task TheIdIsThirtyTwoHexCharacters(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("GET", null, "/binding/path/42");

        var id = response.Headers[Header].ToString();

        Assert.Equal(32, id.Length);
        Assert.All(id, c => Assert.True(Uri.IsHexDigit(c), $"'{c}' is not hex"));
    }

    /// <summary>Two requests are two ids, or it would group unrelated work together.</summary>
    [HardenedTest]
    public async Task TwoRequestsGetTwoIds(ITestWebApp testWebApp) {
        var first = await testWebApp.Request("GET", null, "/binding/path/42");
        var second = await testWebApp.Request("GET", null, "/binding/path/42");

        Assert.NotEqual(first.Headers[Header].ToString(), second.Headers[Header].ToString());
    }

    /// <summary>
    /// A request nothing routed still gets one. A 404 is a thing people ask about, and an id that
    /// only appears on successful responses is missing from every interesting case.
    /// </summary>
    [HardenedTest]
    public async Task AnUnroutedRequestStillCarriesAnId(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("GET", null, "/nothing/is/here");

        Assert.Equal(404, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers[Header].ToString()));
    }

    /// <summary>
    /// So does a refused verb. The header is set on the way in precisely so that a response
    /// produced without reaching a handler still carries it.
    /// </summary>
    [HardenedTest]
    public async Task A405StillCarriesAnId(ITestWebApp testWebApp) {
        var response = await testWebApp.Request("PUT", null, "/binding/path/42");

        Assert.Equal(405, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.Headers[Header].ToString()));
    }
}
