using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// CORS declared on one controller, end to end. Its routes answer an allowed origin, and every
/// other route answers as if the application had no CORS.
/// </summary>
/// <remarks>
/// An integration test because three parts have to agree: the generated table registers the
/// manifest, the startup service reads it, and the route's own filter is in its chain.
/// </remarks>
public class CorsTests
{
    private const string Allowed = "https://app.example.com";

    private static Action<TestWebRequest> From(string origin) =>
        request => request.Headers[KnownHeaders.Origin] = new StringValues(origin);

    private static Action<TestWebRequest> PreflightFor(string method) =>
        request =>
        {
            request.Headers[KnownHeaders.Origin] = new StringValues(Allowed);
            request.Headers[KnownHeaders.Cors.AccessControlRequestMethod] = new StringValues(
                method
            );
        };

    [ModuleTest]
    public async Task ADeclaringRouteAnswersAnAllowedOrigin(ITestWebApp app)
    {
        var response = await app.Get("/cors/greeting", From(Allowed));

        response.Assert.Ok();
        Assert.Equal(
            Allowed,
            response.Headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString()
        );
        Assert.Contains(KnownHeaders.Origin, response.Headers[KnownHeaders.Vary].ToString());
    }

    /// <summary>
    /// What <c>cors.scoped</c> in RequestBench asks: an origin allowed on the declaring routes gets
    /// nothing from a route outside them.
    /// </summary>
    [ModuleTest]
    public async Task ARouteThatDeclaresNoneAnswersWithNoCorsHeaders(ITestWebApp app)
    {
        var response = await app.Get("/", From(Allowed));

        response.Assert.Ok();
        Assert.False(response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
    }

    [ModuleTest]
    public async Task APreflightForADeclaringRouteIsAnswered(ITestWebApp app)
    {
        var response = await app.Request("OPTIONS", null, "/cors/greeting", PreflightFor("GET"));

        Assert.Equal(204, response.StatusCode);
        Assert.Equal(
            Allowed,
            response.Headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString()
        );
        Assert.Contains(
            "GET",
            response.Headers[KnownHeaders.Cors.AccessControlAllowMethods].ToString()
        );
    }

    [ModuleTest]
    public async Task APreflightForARouteThatDeclaresNoneIsRefused(ITestWebApp app)
    {
        var response = await app.Request("OPTIONS", null, "/", PreflightFor("GET"));

        Assert.Equal(204, response.StatusCode);
        Assert.False(response.Headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
    }

    /// <summary>
    /// The filter runs ahead of validation, so a browser can read the 400 it is sent.
    /// </summary>
    [ModuleTest]
    public async Task ARefusalOnADeclaringRouteCarriesTheAllowHeader(ITestWebApp app)
    {
        var response = await app.Post("""{"name":"x","age":5}""", "/cors/sign-up", From(Allowed));

        response.Assert.BadRequest();
        Assert.Equal(
            Allowed,
            response.Headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString()
        );
    }
}
