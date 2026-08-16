namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A cookie a handler sets is one a test can see.
/// </summary>
/// <remarks>
/// It was not. The harness's response recorded cookies into a dictionary nothing serialised, so
/// <c>Response.Cookies.Append</c> compiled, ran, produced no <c>Set-Cookie</c> header, and left
/// cookie behaviour untestable — while the same call worked over HTTP. Nothing in this suite
/// appended a cookie, which is how it stayed hidden.
/// </remarks>
public class ResponseCookieTests {

    [HardenedTest]
    public async Task ACookieSetByAHandlerReachesTheClient(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/cookies/set");

        response.Assert.Ok();

        Assert.True(response.Headers.TryGetValue("Set-Cookie", out var setCookie),
            "the handler appended a cookie and the response carries no Set-Cookie header");
        Assert.Contains(setCookie.ToArray(), value => value!.StartsWith("session=abc123"));
    }

    /// <summary>
    /// The attributes are the security-relevant half. A harness that carried the name and value and
    /// dropped <c>HttpOnly</c> would let a test pass on a cookie the browser treats differently.
    /// </summary>
    [HardenedTest]
    public async Task CookieAttributesReachTheClient(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/cookies/set-with-options");

        response.Assert.Ok();

        var cookie = Assert.Single(response.Headers["Set-Cookie"].ToArray());

        Assert.StartsWith("preference=dark", cookie!);
        Assert.Contains("Path=/app", cookie!);
        Assert.Contains("HttpOnly", cookie!);
        Assert.Contains("Secure", cookie!);
    }
}
