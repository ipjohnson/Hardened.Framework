using Hardened.Requests.Abstract.Authorization;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Authorization;

/// <summary>
/// The <c>WWW-Authenticate</c> header a refused request carries.
///
/// <para>
/// This is a wire format, and the client reading it is a machine deciding what to do next - refresh
/// a token, obtain one, ask the user for a second factor. Getting a parameter wrong sends a client
/// down the wrong branch of that decision, so the exact strings are asserted rather than the shape.
/// </para>
/// </summary>
public class AuthorizationChallengeTests {

    #region no credential

    /// <summary>
    /// RFC 6750 §3 is explicit: a challenge for a request that carried no credential omits
    /// <c>error</c>. Sending <c>invalid_token</c> here tells a client its token was rejected when it
    /// never sent one, which sends it to refresh rather than to obtain.
    /// </summary>
    [Fact]
    public void AuthenticationRequired_CarriesNoErrorParameter() {
        var challenge = AuthorizationChallenge.AuthenticationRequired();

        Assert.Equal(401, challenge.StatusCode);
        Assert.Null(challenge.Error);
        Assert.Equal("Bearer", challenge.HeaderValue);
    }

    [Fact]
    public void AuthenticationRequired_NamesTheRealmWhenThereIsOne() {
        var challenge = AuthorizationChallenge.AuthenticationRequired("pets");

        Assert.Equal("Bearer realm=\"pets\"", challenge.HeaderValue);
    }

    #endregion

    #region invalid credential

    [Fact]
    public void InvalidToken_IsA401NamingTheError() {
        var challenge = AuthorizationChallenge.InvalidToken();

        Assert.Equal(401, challenge.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\"", challenge.HeaderValue);
    }

    [Fact]
    public void InvalidToken_PutsRealmBeforeError() {
        var challenge = AuthorizationChallenge.InvalidToken("pets", "the token expired");

        Assert.Equal(
            "Bearer realm=\"pets\", error=\"invalid_token\", error_description=\"the token expired\"",
            challenge.HeaderValue);
    }

    #endregion

    #region step-up

    /// <summary>
    /// The one refusal of an authenticated caller that is a 401. The error code is RFC 9470's, and
    /// it is what the enum member is named after so the wire and the source use one term.
    /// </summary>
    [Fact]
    public void InsufficientAuthentication_IsA401WithRfc9470sErrorCode() {
        var challenge = AuthorizationChallenge.InsufficientAuthentication();

        Assert.Equal(401, challenge.StatusCode);
        Assert.Equal("Bearer error=\"insufficient_user_authentication\"", challenge.HeaderValue);
    }

    #endregion

    #region insufficient grants

    /// <summary>
    /// A 403 still carries a challenge, and it names what would have worked. The requirement knew
    /// exactly which grants it wanted, so this costs nothing and turns "no" into something the
    /// client can act on.
    /// </summary>
    [Fact]
    public void InsufficientScope_IsA403NamingTheGrantsThatWouldHaveWorked() {
        var challenge = AuthorizationChallenge.InsufficientScope(["pets:read", "pets:write"]);

        Assert.Equal(403, challenge.StatusCode);
        Assert.Equal(
            "Bearer error=\"insufficient_scope\", scope=\"pets:read pets:write\"",
            challenge.HeaderValue);
    }

    /// <summary>
    /// Space-delimited, which is how OAuth writes a scope list everywhere else.
    /// </summary>
    [Fact]
    public void InsufficientScope_JoinsGrantsWithSpaces() {
        var challenge = AuthorizationChallenge.InsufficientScope(["a", "b", "c"]);

        Assert.Contains("scope=\"a b c\"", challenge.HeaderValue);
    }

    [Fact]
    public void InsufficientScope_WithNoGrantsOmitsTheScopeParameter() {
        var challenge = AuthorizationChallenge.InsufficientScope([]);

        Assert.Equal("Bearer error=\"insufficient_scope\"", challenge.HeaderValue);
        Assert.DoesNotContain("scope=", challenge.HeaderValue);
    }

    #endregion

    #region formatting

    /// <summary>
    /// A quote or a backslash in a value would otherwise end the parameter early and make the rest
    /// of the header parse as something else. Nothing reaching here is attacker-controlled today -
    /// grants come from a specification, realms from configuration - but a header built by
    /// concatenation is exactly the thing that stops being true without anyone noticing.
    /// </summary>
    [Theory]
    [InlineData("a\"b", "a\\\"b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("plain", "plain")]
    public void ParameterValuesAreEscapedForAQuotedString(string realm, string expected) {
        var challenge = AuthorizationChallenge.AuthenticationRequired(realm);

        Assert.Equal($"Bearer realm=\"{expected}\"", challenge.HeaderValue);
    }

    [Fact]
    public void Apply_WritesTheHeaderOntoTheResponse() {
        var headers = new Dictionary<string, StringValues>();

        AuthorizationChallenge.InvalidToken().Apply(headers);

        Assert.Equal("Bearer error=\"invalid_token\"", headers["WWW-Authenticate"]);
    }

    /// <summary>
    /// Assigned rather than appended, so a forked or retried chain refusing the same request twice
    /// sends one challenge rather than two.
    /// </summary>
    [Fact]
    public void Apply_TwiceLeavesOneValue() {
        var headers = new Dictionary<string, StringValues>();
        var challenge = AuthorizationChallenge.InvalidToken();

        challenge.Apply(headers);
        challenge.Apply(headers);

        Assert.Equal(new StringValues("Bearer error=\"invalid_token\""), headers["WWW-Authenticate"]);
    }

    #endregion

    #region carried on an exception

    [Fact]
    public void TheExceptionTakesItsStatusFromTheChallenge() {
        Assert.Equal(
            403,
            new AuthorizationException(AuthorizationChallenge.InsufficientScope(["a"])).StatusCode);

        Assert.Equal(
            401,
            new AuthorizationException(AuthorizationChallenge.AuthenticationRequired()).StatusCode);
    }

    [Fact]
    public void TheExceptionAppliesTheChallengesHeader() {
        var headers = new Dictionary<string, StringValues>();

        new AuthorizationException(AuthorizationChallenge.InsufficientScope(["pets:read"]))
            .ApplyHeaders(headers);

        Assert.Equal(
            "Bearer error=\"insufficient_scope\", scope=\"pets:read\"",
            headers["WWW-Authenticate"]);
    }

    /// <summary>
    /// The message is echoed to the caller as the error model's message, so it says that the request
    /// was refused and nothing about which check failed. What the caller legitimately needs travels
    /// in the challenge, where it is machine-readable.
    /// </summary>
    [Fact]
    public void TheExceptionMessageSaysNothingAboutWhyTheCheckFailed() {
        var forbidden = new AuthorizationException(
            AuthorizationChallenge.InsufficientScope(["internal:admin"]));

        Assert.DoesNotContain("internal:admin", forbidden.Message);
        Assert.Equal("This request is not permitted.", forbidden.Message);

        Assert.Equal(
            "This request requires authentication.",
            new AuthorizationException(AuthorizationChallenge.AuthenticationRequired()).Message);
    }

    #endregion
}
