using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The caller has spent their allowance - 429, with the wait before it is worth trying again.
/// </summary>
/// <remarks>
/// <para>
/// <c>Retry-After</c> is not optional here, which is why it is a constructor parameter rather than
/// a nullable one. A 429 without it leaves a client to invent a backoff, and the backoff it invents
/// is either too short - producing a second 429 - or an arbitrary sleep that is longer than the
/// service needed.
/// </para>
/// <para>
/// The seconds come from <see cref="RetryAfter"/>, which is also what
/// <c>RateLimitExceededException</c> uses. Those two are the same refusal reaching the response by
/// the two different routes, and a client should not be able to tell which one answered.
/// </para>
/// </remarks>
[HttpStatus(429)]
public sealed record RateLimited(TimeSpan RetryAfter, string? Detail = null)
    : IHttpStatusResponse, IProvidesResponseHeaders {

    public string Type => ProblemTypes.RateLimited;

    public string Title => "Too Many Requests";

    public int Status => 429;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        headers[KnownHeaders.RetryAfter] = Responses.RetryAfter.HeaderValue(RetryAfter);
    }
}
