using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The service cannot answer right now and expects to be able to later - 503.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="After"/> is genuinely optional, unlike <c>RateLimited</c>'s. A rate limiter always
/// knows when the allowance returns because it is the thing holding the window; a service shedding
/// load or waiting on a dependency frequently does not, and inventing a number there would be a
/// guess presented to the client as a fact. No header is the honest answer when there is nothing to
/// say.
/// </para>
/// <para>
/// This is the one built-in problem that is a server fault, and it is deliberately not folded into
/// the pipeline's generic 500: a 503 tells a client the request itself was fine and retrying it is
/// sensible, which is the opposite of what a 500 tells it.
/// </para>
/// </remarks>
[HttpStatus(503)]
public sealed record ServiceUnavailable(TimeSpan? After = null, string? Detail = null)
    : IHttpStatusResponse, IProvidesResponseHeaders, IDeclaresStatus {

    public string Type => ProblemTypes.ServiceUnavailable;

    public string Title => "Service Unavailable";

    public static int StatusCode => 503;

    public int Status => StatusCode;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        if (After is { } after) {
            headers[KnownHeaders.RetryAfter] = RetryAfter.HeaderValue(after);
        }
    }
}
