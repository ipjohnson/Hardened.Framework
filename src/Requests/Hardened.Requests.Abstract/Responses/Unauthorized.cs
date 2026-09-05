using Hardened.Requests.Abstract.Authorization;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The caller has not established who they are - 401, with the challenge that says how to.
/// </summary>
/// <remarks>
/// <para>
/// The challenge is formatted by <see cref="AuthorizationChallenge"/> rather than here, which is
/// the whole reason that type exists: a refusal reaches the response two ways - thrown, or handed
/// to the response directly by a filter placed ahead of serialization - and both must produce
/// byte-identical headers. Returning one is now a third way, and it formats nothing of its own for
/// the same reason.
/// </para>
/// <para>
/// A 401 without <c>WWW-Authenticate</c> tells a client it must authenticate without saying how,
/// and RFC 6750 requires the challenge, so the default is
/// <see cref="AuthorizationChallenge.AuthenticationRequired"/> rather than no header. A caller who
/// knows more - that a token was presented and rejected, that a scope is missing - passes the
/// challenge that says so, because those send a client to different remedies.
/// </para>
/// </remarks>
[HttpStatus(401)]
public sealed record Unauthorized(string? Detail = null, AuthorizationChallenge? Challenge = null)
    : IHttpStatusResponse, IProvidesResponseHeaders, IDeclaresStatus {

    public string Type => ProblemTypes.Unauthorized;

    public string Title => "Unauthorized";

    public static int StatusCode => 401;

    public int Status => StatusCode;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        var challenge = Challenge ?? AuthorizationChallenge.AuthenticationRequired();

        headers[AuthorizationChallenge.HeaderName] = challenge.HeaderValue;
    }
}
