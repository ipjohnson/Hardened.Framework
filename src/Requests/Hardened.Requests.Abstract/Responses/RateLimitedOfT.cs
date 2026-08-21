using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A 429 carrying a body the caller supplies.
/// </summary>
/// <remarks>
/// <para>
/// The generic form of <see cref="RateLimited"/>, for a caller who already has a payload type and does
/// not want a Hardened-shaped problem document. Both are the same problem kind and carry the same
/// <c>type</c> URI - that identifies what went wrong, not what shape the body is.
/// </para>
/// <para>
/// <b>The body is <see cref="Body"/>, not this record.</b> It implements
/// <see cref="ICarriesResponseBody"/>, so the generated dispatch sends the payload rather than a
/// wrapper with the payload nested inside it.
/// </para>
/// <para>
/// This is the hand-written equivalent of what the specification-first emitter generates for a
/// declared status - <c>GetPetNotFound(ApiError Body)</c> - so the two front ends describe a
/// declared error the same way, and two statuses sharing one payload type are two distinct case
/// types rather than the CS0457 that <c>RateLimited, RateLimited</c> would be.
/// </para>
/// </remarks>
[HttpStatus(429)]
public sealed record RateLimited<T>(TimeSpan RetryAfter, T Body)
    : IHttpStatusResponse, ICarriesResponseBody, IProvidesResponseHeaders {

    public string Type => ProblemTypes.RateLimited;

    public string Title => "Too Many Requests";

    public int Status => 429;

    object? ICarriesResponseBody.Body => Body;
    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        headers[KnownHeaders.RetryAfter] = Responses.RetryAfter.HeaderValue(RetryAfter);
    }
}
