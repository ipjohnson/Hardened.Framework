using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A resource was created - 201, its representation, and where it now lives.
/// </summary>
/// <remarks>
/// <para>
/// The only built-in response that is generic, because a creation is the one case where the status
/// and the payload are genuinely independent: what was created is the caller's type, and 201 is the
/// framework's. The problem types have no such split - their payload <em>is</em> the problem.
/// </para>
/// <para>
/// <see cref="Location"/> is required. A 201 that does not say where the thing is forces the caller
/// to guess a URL or re-query a collection to find what it just made, and RFC 9110 names the header
/// as how a 201 identifies its resource.
/// </para>
/// <para>
/// <b>The body is <see cref="Value"/>, not this record.</b> Serializing the wrapper would put the
/// caller's resource under a <c>value</c> member and the location in the body as well as the
/// header, which is not what a 201 looks like anywhere. Unwrapping is the response-mode plumbing's
/// job and arrives with it; until then this type is the declaration, and what reads it is the
/// generator.
/// </para>
/// </remarks>
[HttpStatus(201)]
public sealed record Created<T>(T Value, string Location)
    : IHttpStatusResponse, IProvidesResponseHeaders {

    public int Status => 201;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        headers[KnownHeaders.Location] = Location;
    }
}
