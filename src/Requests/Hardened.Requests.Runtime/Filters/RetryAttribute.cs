using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Re-runs the handler when an attempt fails.
/// </summary>
/// <remarks>
/// <para>
/// Narrow on purpose. By default it declines client errors and refuses to touch a non-idempotent
/// verb at all, because the failures worth retrying in a request pipeline are a small subset of the
/// failures that happen: a dependency that was briefly unreachable, not a request that was wrong.
/// Retrying around an <em>outbound</em> call is the more usual want, and belongs with the call
/// rather than here - a retry at this level holds the connection and a request slot for the whole
/// backoff, which is amplification arriving exactly when the system can least absorb it.
/// </para>
/// <para>
/// <b>On a streaming handler a retry covers the call, never the enumeration.</b> A handler
/// returning <c>IAsyncEnumerable&lt;T&gt;</c> returns a lazy sequence, and an attempt ends when the
/// call does; the items are produced afterwards, by the filter that writes them, outside any
/// attempt. So a failure producing the sequence is retried and a failure while enumerating it is
/// not - it ends the stream, and the client comes back with <c>Last-Event-ID</c>. Making the
/// enumeration safe to run again is the author's job, and the event id is the tool for it.
/// </para>
/// <para>
/// See <see cref="FilterOrder.Retry"/> for why the filter sits where it does, and what that costs.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RetryAttribute : Attribute, IRequestFilterProvider {

    /// <summary>
    /// Total attempts, including the first. Three by default.
    /// </summary>
    /// <remarks>
    /// A total rather than a count of extra tries, which is what <see cref="Retries"/> always
    /// meant despite its name. One or less runs the handler once and retries nothing, rather than
    /// running it zero times - which is what the previous implementation did with a zero here, and
    /// it reported success.
    /// </remarks>
    public int Attempts { get; set; } = 3;

    /// <summary>
    /// The older spelling of <see cref="Attempts"/>, kept working because it is what every existing
    /// <c>[Retry]</c> in the wild is written with. The same value, under the name that described it
    /// less well.
    /// </summary>
    public int Retries {
        get => Attempts;
        set => Attempts = value;
    }

    /// <summary>
    /// Base backoff in milliseconds. The wait before attempt <c>n</c> is drawn from
    /// <c>[0, SleepTime * 2^(n-2))</c>; zero waits not at all.
    /// </summary>
    public int SleepTime { get; set; } = 500;

    /// <summary>
    /// Milliseconds across all attempts, after which no further attempt starts. Zero for no bound.
    /// </summary>
    /// <remarks>
    /// The caller is waiting for every one of these attempts. Without a bound, attempts times
    /// backoff is how long a request can occupy a slot while returning nothing.
    /// </remarks>
    public int TotalBudget { get; set; } = 10_000;

    /// <summary>
    /// Retry a <c>POST</c> or <c>PATCH</c> too. Off, and worth leaving off unless the handler is
    /// idempotent by some other means - a retried write is a second write.
    /// </summary>
    public bool AllowNonIdempotent { get; set; }

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return new RequestFilterInfo(
            _ => new RetryFilter(Attempts, SleepTime, TotalBudget, AllowNonIdempotent),
            FilterOrder.Retry,
            nameof(RetryFilter));
    }
}
