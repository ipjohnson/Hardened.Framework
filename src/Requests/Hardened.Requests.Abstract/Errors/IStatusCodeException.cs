using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Errors;

/// <summary>
/// An exception that names the status - and any headers - the response should carry.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StatusCodeException"/> already let an exception name its status, which is most of
/// this. What it could not do is name a <em>header</em>, and some statuses are not well-formed
/// without one: a 401 that carries no <c>WWW-Authenticate</c> tells a client it needs to
/// authenticate without saying how, and RFC 6750 requires the challenge. Authorization is the
/// feature that needed it first; it is an interface rather than more surface on the class so the
/// next status that travels with a header - a 429 with <c>Retry-After</c>, a 405 with <c>Allow</c> -
/// does not need the mechanism widened again.
/// </para>
/// <para>
/// Checked by <c>ExceptionToModelConverter</c> before its type-based classification, because an
/// exception that states its own status is more specific than "not a client error, so 500".
/// </para>
/// </remarks>
public interface IStatusCodeException {
    /// <summary>The status the response carries.</summary>
    int StatusCode { get; }

    /// <summary>
    /// Adds whatever headers the status requires. Called before the body is written.
    /// </summary>
    /// <remarks>
    /// Assigns rather than appends, so a retried or forked request that produces the same failure
    /// twice does not send the header twice.
    /// </remarks>
    void ApplyHeaders(IDictionary<string, StringValues> headers);
}
