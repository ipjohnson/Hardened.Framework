using System.Globalization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
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
public class RateLimitExceededException : StatusCodeException
{
    private readonly RateLimitDecision _decision;
    private readonly RateLimitPolicy _policy;

    public RateLimitExceededException(RateLimitDecision decision, RateLimitPolicy policy)
        : base(429, value: null, message: "Rate limit exceeded.")
    {
        _decision = decision;
        _policy = policy;
    }

    public override void ApplyHeaders(IDictionary<string, StringValues> headers)
    {
        // Seconds, rounded up: rounding down would invite the caller back a moment before the
        // allowance exists and produce a second 429.
        //
        // Shared with the RateLimited response type rather than rounded here, because those two are
        // the same refusal reaching the response by different routes and a client must not be able
        // to tell which one answered.
        var seconds = RetryAfter.Seconds(_decision.RetryAfter);

        headers[KnownHeaders.RetryAfter] = seconds.ToString(CultureInfo.InvariantCulture);

        ApplyRateLimitHeaders(headers, _decision, _policy, seconds);
    }

    /// <summary>
    /// The rate limit fields, which a client uses to pace itself rather than discover the limit by
    /// hitting it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both generations of draft-ietf-httpapi-ratelimit-headers. <c>RateLimit-Limit</c>,
    /// <c>RateLimit-Remaining</c> and <c>RateLimit-Reset</c> are what clients written against the
    /// early drafts read, and each holds one limit, so the last limit to write wins.
    /// <c>RateLimit-Policy</c> and <c>RateLimit</c> are the current draft's lists, which name each
    /// limit, so a second limit on the handler adds an item rather than replacing the first.
    /// </para>
    /// </remarks>
    internal static void ApplyRateLimitHeaders(
        IDictionary<string, StringValues> headers,
        RateLimitDecision decision,
        RateLimitPolicy policy,
        int resetSeconds
    )
    {
        headers["RateLimit-Limit"] = decision.Limit.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Remaining"] = decision.Remaining.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Reset"] = resetSeconds.ToString(CultureInfo.InvariantCulture);

        var name = StructuredString(policy.Name);
        var window = (long)Math.Ceiling(policy.Window.TotalSeconds);

        PutItem(
            headers,
            "RateLimit-Policy",
            name,
            string.Create(CultureInfo.InvariantCulture, $"{name};q={decision.Limit};w={window}")
        );
        PutItem(
            headers,
            "RateLimit",
            name,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{name};r={decision.Remaining};t={resetSeconds}"
            )
        );
    }

    /// <summary>
    /// Adds <paramref name="item"/> to the list in <paramref name="field"/>, in place of an item
    /// already there under the same name.
    /// </summary>
    /// <remarks>
    /// The same name arrives twice when a limit that allowed the request wrote its item and the
    /// refusal is written over the same headers, or when two limits on one handler share a name.
    /// </remarks>
    private static void PutItem(
        IDictionary<string, StringValues> headers,
        string field,
        string name,
        string item
    )
    {
        var items = new List<string>();

        if (headers.TryGetValue(field, out var existing))
        {
            foreach (var value in existing)
            {
                foreach (var present in SplitList(value))
                {
                    if (!present.StartsWith(name + ";", StringComparison.Ordinal))
                    {
                        items.Add(present);
                    }
                }
            }
        }

        items.Add(item);

        headers[field] = string.Join(", ", items);
    }

    /// <summary>
    /// The members of a structured field list, split at the commas outside a quoted string.
    /// </summary>
    private static IEnumerable<string> SplitList(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield break;
        }

        var start = 0;
        var quoted = false;

        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '\\' when quoted:
                    i++;
                    break;
                case '"':
                    quoted = !quoted;
                    break;
                case ',' when !quoted:
                    yield return value[start..i].Trim();
                    start = i + 1;
                    break;
            }
        }

        yield return value[start..].Trim();
    }

    /// <summary>
    /// <paramref name="value"/> as an RFC 9651 string. A character the format has no room for, which
    /// is anything outside printable ASCII, becomes <c>?</c>.
    /// </summary>
    private static string StructuredString(string? value)
    {
        var builder = new System.Text.StringBuilder("\"");

        foreach (var character in value ?? "")
        {
            if (character is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character is >= ' ' and <= '~' ? character : '?');
        }

        return builder.Append('"').ToString();
    }
}
