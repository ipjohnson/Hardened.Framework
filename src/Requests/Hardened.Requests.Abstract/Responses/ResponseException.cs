using Hardened.Requests.Abstract.Errors;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A response type, thrown - which is how these reach the wire before the response modes exist.
/// </summary>
/// <remarks>
/// <para>
/// The built-in types are declarations first: what a response set is built from, and what the
/// generator reads a status off. But a handler in throws mode returns one type and has nowhere to
/// put a second, so without this they would be useful only after the union work landed. Throwing
/// one costs nothing extra - <c>ExceptionToModelConverter</c> already answers an
/// <see cref="IStatusCodeException"/> with its status, its headers and its declared body - and it
/// makes the whole set worth having today.
/// </para>
/// <para>
/// It derives from <see cref="StatusCodeException"/> rather than implementing
/// <see cref="IStatusCodeException"/> directly, because the converter reads a declared body off
/// <c>StatusCodeException.Value</c> specifically. Implementing the interface alone would produce
/// the right status and the right headers with an <c>ErrorModel</c> body - the response type
/// discarded at the last step, silently.
/// </para>
/// <para>
/// The status comes from the response rather than from a parameter, so there is no way to throw a
/// <c>NotFound</c> as a 409. That is the point of the type carrying its own status; letting a call
/// site override it here would reintroduce exactly the drift the four-layer resolution exists to
/// order.
/// </para>
/// </remarks>
public class ResponseException : StatusCodeException {

    private readonly IHttpStatusResponse _response;

    public ResponseException(IHttpStatusResponse response, string? message = null)
        : base(
            (response ?? throw new ArgumentNullException(nameof(response))).Status,
            value: response.HasBody ? response : null,
            message: message ?? "The request produced status " + response.Status + ".") {
        _response = response;
    }

    /// <summary>The response this was thrown for.</summary>
    public IHttpStatusResponse Response => _response;

    /// <summary>
    /// Whatever headers the response contributes, and nothing of this type's own.
    /// </summary>
    /// <remarks>
    /// A <c>Unauthorized</c> formats its challenge through <c>AuthorizationChallenge</c> and a
    /// <c>RateLimited</c> its wait through <c>RetryAfter</c>, and both do so identically whether
    /// they were thrown or returned. Deciding anything here would be the second place a header is
    /// written and the one that drifts.
    /// </remarks>
    public override void ApplyHeaders(IDictionary<string, StringValues> headers) {
        if (_response is IProvidesResponseHeaders provider) {
            provider.ApplyHeaders(headers);
        }
    }
}
