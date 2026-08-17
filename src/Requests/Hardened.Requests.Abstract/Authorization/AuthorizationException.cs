using Hardened.Requests.Abstract.Errors;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// A refused request, carrying the challenge that says why.
/// </summary>
/// <remarks>
/// <para>
/// Derives from <see cref="StatusCodeException"/> so the existing exception path answers it - status
/// from the challenge, headers through <see cref="ApplyHeaders"/>, body from the pipeline's usual
/// error model. Nothing about the refusal is special-cased in the converter.
/// </para>
/// <para>
/// <b>The message is deliberately unspecific.</b> It is echoed to the caller as the error model's
/// message, so it says that the request was refused and nothing about why the check failed. What the
/// caller legitimately needs - which grants would have worked - travels in the challenge header,
/// where it is machine-readable and where the design already decided to disclose it.
/// </para>
/// </remarks>
public class AuthorizationException : StatusCodeException {
    public AuthorizationException(AuthorizationChallenge challenge, string? message = null)
        : base(challenge.StatusCode, value: null, message: message ?? MessageFor(challenge)) {
        Challenge = challenge;
    }

    public AuthorizationChallenge Challenge { get; }

    public override void ApplyHeaders(IDictionary<string, StringValues> headers) =>
        Challenge.Apply(headers);

    private static string MessageFor(AuthorizationChallenge challenge) =>
        challenge.StatusCode == 401
            ? "This request requires authentication."
            : "This request is not permitted.";
}
