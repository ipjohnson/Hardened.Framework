using System.Net;
using Hardened.Requests.Runtime.Validation;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// What the transport lets a test send that the harness could not: a body the deserializer
/// refuses, through an <see cref="HttpClient"/> and through <see cref="TestWebRequest.RawBody(string, string)"/>.
/// </summary>
public class HttpClientTransportTests {

    [HardenedTest]
    public async Task MalformedJsonThroughAnHttpClientAnswersTheValidationStatus(ITestWebApp app) {
        using var client = app.CreateHttpClient();
        using var content = new StringContent("{\"name\":", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/registration", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ValidationError", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [HardenedTest]
    public async Task ARawBodyOnTheRequestAnswersTheValidationStatus(ITestWebApp app) {
        var response = await app.Post(new object(), "/registration", request => request.RawBody("{\"name\":"));

        response.Assert.BadRequest();
        Assert.Equal("ValidationError", response.Deserialize<RequestValidationError>().Type);
    }

    [HardenedTest]
    public async Task ARawStringThroughRequestAnswersTheValidationStatus(ITestWebApp app) {
        var response = await app.Request("POST", "{\"name\":", "/registration");

        response.Assert.BadRequest();
    }

    /// <summary>
    /// The path the client sends, escapes and all, is decoded once and the same way as
    /// <c>app.Get</c>: <c>EncodedPathTests</c> holds the harness to Kestrel's table, and this holds
    /// the handler to the harness on the same rows.
    /// </summary>
    [HardenedTest]
    public async Task AnEncodedPathAnswersTheSameThroughTheHandlerAndTheHarness(ITestWebApp app) {
        using var client = app.CreateHttpClient();

        foreach (var (encoded, expected) in new[] {
                     ("%20", " "), ("caf%C3%A9", "caf\u00e9"), ("a%25b", "a%b"), ("a%2Fb", "a%2Fb"), ("a+b", "a+b")
                 }) {
            var direct = await app.Get("/binding/path/" + encoded);

            using var response = await client.GetAsync("/binding/path/" + encoded, TestContext.Current.CancellationToken);

            var viaHandler = System.Text.Json.JsonSerializer.Deserialize<string>(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            Assert.Equal(expected, direct.Deserialize<string>());
            Assert.Equal(expected, viaHandler);
        }
    }
}
