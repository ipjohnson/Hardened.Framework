using Hardened.IntegrationTests.Jwt.SUT;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Jwt.Testing;

namespace Hardened.IntegrationTests.Jwt.SUT.Tests;

/// <summary>
/// Tokens signed by the test issuer, through the whole pipeline: the source, the validator, the
/// authorization filter and the challenge it answers with.
/// </summary>
public class JwtBearerTests
{
    private const string Challenge = "WWW-Authenticate";

    private static Action<TestWebRequest> Bearer(string token) =>
        request => request.Headers[KnownHeaders.Authorization] = "Bearer " + token;

    [ModuleTest]
    public async Task AValidTokenIsTheCaller(ITestWebApp app, TestJwtIssuer issuer)
    {
        var token = issuer.Token(jwt =>
        {
            jwt.Subject = "ada";
            jwt.Grants = ["notes:write", "notes:read"];
        });

        var response = await app.Get("/me", Bearer(token));

        response.Assert.Ok();

        var caller = response.Deserialize<Caller>()!;

        Assert.Equal("ada", caller.Subject);
        Assert.Equal(TestJwtIssuer.DefaultIssuer, caller.Issuer);
        Assert.Equal(["notes:read", "notes:write"], caller.Grants);
    }

    /// <summary>
    /// No token at all: the challenge carries no <c>error</c>, because there is no token to have
    /// been wrong about.
    /// </summary>
    [ModuleTest]
    public async Task ARequestWithNoTokenIsToldToAuthenticate(ITestWebApp app)
    {
        var response = await app.Get("/me");

        Assert.Equal(401, response.StatusCode);
        Assert.Equal("Bearer", response.Headers[Challenge].ToString());
    }

    [ModuleTest]
    public async Task AnExpiredTokenIsToldItIsInvalid(ITestWebApp app, TestJwtIssuer issuer)
    {
        var expires = DateTime.UtcNow.AddMinutes(-10);

        var response = await app.Get("/me", Bearer(issuer.Token(jwt => jwt.Expires = expires)));

        Assert.Equal(401, response.StatusCode);
        Assert.StartsWith(
            "Bearer error=\"invalid_token\", error_description=\"The token expired at ",
            response.Headers[Challenge].ToString()
        );
    }

    [ModuleTest]
    public async Task ATokenForAnotherAudienceIsToldItIsInvalid(
        ITestWebApp app,
        TestJwtIssuer issuer
    )
    {
        var response = await app.Get(
            "/me",
            Bearer(issuer.Token(jwt => jwt.Audience = "someone-else"))
        );

        Assert.Equal(401, response.StatusCode);
        Assert.Equal(
            "Bearer error=\"invalid_token\", error_description=\"The token is not for this audience.\"",
            response.Headers[Challenge].ToString()
        );
    }

    [ModuleTest]
    public async Task ATokenSignedByAnUntrustedKeyIsToldItIsInvalid(
        ITestWebApp app,
        TestJwtIssuer issuer
    )
    {
        var response = await app.Get(
            "/me",
            Bearer(issuer.Token(jwt => jwt.SignedByUntrustedKey = true))
        );

        Assert.Equal(401, response.StatusCode);
        Assert.StartsWith("Bearer error=\"invalid_token\"", response.Headers[Challenge].ToString());
    }

    /// <summary>
    /// A route that requires no caller serves a request whose token was refused, as if it had sent
    /// none.
    /// </summary>
    [ModuleTest]
    public async Task APublicRouteServesARequestWhoseTokenWasRefused(
        ITestWebApp app,
        TestJwtIssuer issuer
    )
    {
        var response = await app.Get(
            "/status",
            Bearer(issuer.Token(jwt => jwt.Expires = DateTime.UtcNow.AddMinutes(-10)))
        );

        response.Assert.Ok();

        Assert.Equal("up", response.Deserialize<string>());
    }

    [ModuleTest]
    public async Task AGrantFromTheScopeClaimSatisfiesARequirement(
        ITestWebApp app,
        TestJwtIssuer issuer
    )
    {
        var response = await app.Post(
            "",
            "/notes",
            Bearer(issuer.Token(jwt => jwt.Grants = ["notes:write"]))
        );

        response.Assert.Ok();
    }

    [ModuleTest]
    public async Task AValidTokenWithoutTheGrantIsForbidden(ITestWebApp app, TestJwtIssuer issuer)
    {
        var response = await app.Post(
            "",
            "/notes",
            Bearer(issuer.Token(jwt => jwt.Grants = ["notes:read"]))
        );

        Assert.Equal(403, response.StatusCode);
        Assert.Contains("insufficient_scope", response.Headers[Challenge].ToString());
    }
}
