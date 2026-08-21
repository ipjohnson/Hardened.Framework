using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// Something that contributes the headers its status is not well-formed without.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of <c>IStatusCodeException</c> rather than written beside it. That interface already
/// had exactly this member, for exactly this reason - a 401 that carries no
/// <c>WWW-Authenticate</c> tells a client it needs to authenticate without saying how - and a
/// returned response needs the same thing a thrown one does. Two interfaces with the same method
/// would mean two call sites, and the second one is the one that gets forgotten when a status is
/// added.
/// </para>
/// <para>
/// <c>IStatusCodeException</c> now derives from this, so everything that already implemented it
/// still does and every writer of a response calls one method.
/// </para>
/// </remarks>
public interface IProvidesResponseHeaders {

    /// <summary>
    /// Adds whatever headers the status requires. Called before the body is written.
    /// </summary>
    /// <remarks>
    /// Assigns rather than appends, so a retried or forked request that produces the same response
    /// twice does not send the header twice.
    /// </remarks>
    void ApplyHeaders(IDictionary<string, StringValues> headers);
}
