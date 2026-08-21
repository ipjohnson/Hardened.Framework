using System.Globalization;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The <c>Retry-After</c> value, as the delta-seconds form.
/// </summary>
/// <remarks>
/// <para>
/// Shared because two statuses now write this header and they must agree. <c>RateLimited</c> and
/// <c>ServiceUnavailable</c> are different problems that happen to answer the same question, and
/// <c>RateLimitExceededException</c> was already answering it a third time - with this rounding,
/// arrived at because rounding down invites the caller back a moment before the allowance exists
/// and produces a second refusal.
/// </para>
/// <para>
/// Seconds rather than an HTTP-date. RFC 9110 permits both, and a delta needs no agreement about
/// clocks between a caller and a server that may be behind a proxy that rewrites neither.
/// </para>
/// </remarks>
public static class RetryAfter {

    /// <summary>
    /// <paramref name="delay"/> as whole seconds, rounded up, never below one.
    /// </summary>
    /// <remarks>
    /// Never zero: <c>Retry-After: 0</c> reads as "immediately", which is the one answer that is
    /// certainly wrong when the reason for the header is that the caller must wait.
    /// </remarks>
    public static int Seconds(TimeSpan delay) =>
        Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));

    /// <summary>
    /// <paramref name="delay"/> as the header's string value.
    /// </summary>
    public static string HeaderValue(TimeSpan delay) =>
        Seconds(delay).ToString(CultureInfo.InvariantCulture);
}
