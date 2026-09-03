using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The path exists and this method is not one it answers - 405, and the ones that are.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Allow"/> is required, not optional. RFC 9110 requires the header on a 405, and it is
/// the only part of the response that tells the caller what to do instead - the same reasoning
/// <c>IMethodNotAllowedHandler</c> already states for the 405 the router itself answers.
/// </para>
/// <para>
/// Bodyless, which is what <c>MethodNotAllowedHandler</c> sends: the response is the status and
/// the header. <c>IExecutionResponse</c> cites this status as the motivating case for a response
/// that opts out of a body at all. An application that wants one uses
/// <see cref="MethodNotAllowed{T}"/>, which is the other thing that interface's own documentation
/// says a 405 may carry.
/// </para>
/// </remarks>
[HttpStatus(405)]
public sealed record MethodNotAllowed(string Allow)
    : IHttpStatusResponse, IProvidesResponseHeaders {

    public int Status => 405;

    public bool HasBody => false;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        headers[KnownHeaders.Allow] = Allow;
    }
}
