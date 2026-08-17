using System.Globalization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.RateLimiting;

/// <summary>
/// The refusal, as an exception so the pipeline's own error path writes it.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IStatusCodeException"/> because a 429 is not well-formed without
/// <c>Retry-After</c>, and that interface exists exactly so a status can carry the headers it
/// needs. Its own documentation names "a 429 with <c>Retry-After</c>" as the case it was widened
/// for.
/// </para>
/// <para>
/// Being an exception rather than a hand-written response is what lets the filter use the
/// refuse-and-continue pattern: a filter ahead of serialization records this on the response and
/// calls <c>Next</c>, and the serialization filter finds a request already decided, reads no body,
/// invokes no handler, and writes the refusal on its way out.
/// </para>
/// </remarks>
public class RateLimitExceededException : StatusCodeException {
    private readonly RateLimitDecision _decision;

    public RateLimitExceededException(RateLimitDecision decision)
        : base(429, value: null, message: "Rate limit exceeded.") {
        _decision = decision;
    }

    public override void ApplyHeaders(IDictionary<string, StringValues> headers) {
        // Seconds, rounded up: rounding down would invite the caller back a moment before the
        // allowance exists and produce a second 429.
        var seconds = Math.Max(1, (int)Math.Ceiling(_decision.RetryAfter.TotalSeconds));

        headers[KnownHeaders.RetryAfter] = seconds.ToString(CultureInfo.InvariantCulture);

        ApplyRateLimitHeaders(headers, _decision, seconds);
    }

    /// <summary>
    /// The <c>RateLimit-*</c> family, which a client uses to pace itself rather than discover the
    /// limit by hitting it.
    /// </summary>
    internal static void ApplyRateLimitHeaders(
        IDictionary<string, StringValues> headers, RateLimitDecision decision, int resetSeconds) {
        headers["RateLimit-Limit"] = decision.Limit.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Remaining"] = decision.Remaining.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Reset"] = resetSeconds.ToString(CultureInfo.InvariantCulture);
    }
}
