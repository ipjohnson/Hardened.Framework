using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The headers a status is not well-formed without, and the one place each is formatted.
///
/// <para>
/// These are wire formats read by a machine deciding what to do next, so the exact strings are
/// asserted rather than the shape. The other thing under test is that a response contributes the
/// <em>same</em> header whether it was returned or thrown - the two routes existed before these
/// types did, and a client must not be able to tell which one answered.
/// </para>
/// </summary>
public class ResponseHeaderTests {

    #region Unauthorized

    /// <summary>
    /// A 401 that carries no challenge tells a client it must authenticate without saying how, so
    /// the default is a challenge rather than no header.
    /// </summary>
    [Fact]
    public void Unauthorized_ChallengesEvenWhenNoneWasGiven() {
        var headers = HeadersOf(new Unauthorized());

        Assert.Equal("Bearer", headers[AuthorizationChallenge.HeaderName]);
    }

    /// <summary>
    /// Formatted by AuthorizationChallenge rather than here. The same challenge reaches the wire
    /// from a filter and from a thrown refusal, and only one formatter guarantees they agree.
    /// </summary>
    [Fact]
    public void Unauthorized_UsesTheChallengeItWasGiven() {
        var challenge = AuthorizationChallenge.InvalidToken(realm: "pets");

        var headers = HeadersOf(new Unauthorized(Challenge: challenge));

        Assert.Equal(challenge.HeaderValue, headers[AuthorizationChallenge.HeaderName]);
    }

    #endregion

    #region Retry-After

    [Fact]
    public void RateLimited_WritesRetryAfterInWholeSeconds() {
        var headers = HeadersOf(new RateLimited(TimeSpan.FromSeconds(30)));

        Assert.Equal("30", headers["Retry-After"]);
    }

    /// <summary>
    /// Rounded up. Rounding down invites the caller back a moment before the allowance exists and
    /// produces a second refusal - which is the reason the rate limiter's own exception has always
    /// rounded this way, and why both now go through one function.
    /// </summary>
    [Fact]
    public void RateLimited_RoundsAPartialSecondUp() {
        var headers = HeadersOf(new RateLimited(TimeSpan.FromMilliseconds(1200)));

        Assert.Equal("2", headers["Retry-After"]);
    }

    /// <summary>
    /// Never zero. <c>Retry-After: 0</c> reads as "immediately", which is certainly wrong when the
    /// whole reason for the header is that the caller must wait.
    /// </summary>
    [Fact]
    public void RateLimited_NeverWritesZero() {
        var headers = HeadersOf(new RateLimited(TimeSpan.Zero));

        Assert.Equal("1", headers["Retry-After"]);
    }

    /// <summary>
    /// A rate limiter always knows when the window reopens; a service shedding load frequently does
    /// not, and a fabricated number would be a guess presented as a fact.
    /// </summary>
    [Fact]
    public void ServiceUnavailable_WritesNoRetryAfterWhenItHasNothingToSay() {
        var headers = HeadersOf(new ServiceUnavailable());

        Assert.False(headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public void ServiceUnavailable_WritesRetryAfterWhenItDoes() {
        var headers = HeadersOf(new ServiceUnavailable(TimeSpan.FromMinutes(2)));

        Assert.Equal("120", headers["Retry-After"]);
    }

    #endregion

    #region Location

    [Fact]
    public void Created_WritesTheLocationOfWhatItMade() {
        var headers = HeadersOf(new Created<string>("value", "/todos/7"));

        Assert.Equal("/todos/7", headers["Location"]);
    }

    [Fact]
    public void Accepted_WritesLocationOnlyWhenThereIsSomewhereToWatch() {
        Assert.False(HeadersOf(new Accepted()).ContainsKey("Location"));
        Assert.Equal("/jobs/7", HeadersOf(new Accepted("/jobs/7"))["Location"]);
    }

    #endregion

    #region assigns rather than appends

    /// <summary>
    /// The contract the interface states: a retried or forked request producing the same response
    /// twice must not send the header twice.
    /// </summary>
    [Fact]
    public void ApplyHeaders_AssignsRatherThanAppends() {
        var headers = new Dictionary<string, StringValues>();
        var response = new RateLimited(TimeSpan.FromSeconds(5));

        response.ApplyHeaders(headers);
        response.ApplyHeaders(headers);

        Assert.Equal("5", headers["Retry-After"]);
    }

    #endregion

    #region thrown and returned agree

    /// <summary>
    /// The reason ResponseException formats nothing of its own. If it decided any header here,
    /// that would be a second place one is written, and the two would drift.
    /// </summary>
    [Fact]
    public void ResponseException_ContributesTheSameHeadersAsTheResponse() {
        var response = new Unauthorized(Challenge: AuthorizationChallenge.InvalidToken());

        var returned = new Dictionary<string, StringValues>();
        var thrown = new Dictionary<string, StringValues>();

        response.ApplyHeaders(returned);
        new ResponseException(response).ApplyHeaders(thrown);

        Assert.Equal(returned, thrown);
    }

    [Fact]
    public void ResponseException_AddsNothingForAResponseThatContributesNoHeaders() {
        var headers = new Dictionary<string, StringValues>();

        new ResponseException(new Conflict()).ApplyHeaders(headers);

        Assert.Empty(headers);
    }

    #endregion

    private static Dictionary<string, StringValues> HeadersOf(IProvidesResponseHeaders response) {
        var headers = new Dictionary<string, StringValues>();

        response.ApplyHeaders(headers);

        return headers;
    }
}
