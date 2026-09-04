using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Abstract.Timeouts;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Bounds how long an operation may take, written on the operation, on its class, or on an
/// assembly.
///
/// <code>
/// [Get("/rates/{symbol}")]
/// [Timeout(Milliseconds = 2000)]
/// public Task&lt;Rate&gt; Read(string symbol, CancellationToken cancellationToken) =&gt;
///     _upstream.Latest(symbol, cancellationToken);
/// </code>
///
/// <code>
/// // Every handler in this library, unless one says otherwise.
/// [assembly: Timeout(Milliseconds = 2000, Status = 503, RetryAfterSeconds = 30)]
/// </code>
///
/// <para>
/// <b>A declaration rather than a filter.</b> This states a policy and installs nothing; the chain
/// builder reads <c>IExecutionRequestHandlerInfo.Timeout</c> and installs one filter from whatever
/// the cascade resolved. So the operation, the class, the assembly, the entry point's default and
/// an <c>IRequestTimeoutConvention</c> all reach the pipeline the same way, and there is one budget
/// per handler however many places could have set it. See <c>TimeoutResolver</c> for the order.
/// </para>
/// <para>
/// Nearest wins, so a method loosens or tightens what its class declared and a class does the same
/// to its assembly. Nothing is combined: two budgets do not compose into a third.
/// </para>
/// <para>
/// <b>Cancellation is cooperative.</b> The budget cancels the token the handler binds; it does not
/// take a thread back. A handler that blocks, or awaits something without passing the token, runs
/// to completion and answers late.
/// </para>
/// <para>
/// <b>Nothing can be done once the body has started.</b> A deadline that fires mid-stream cuts the
/// body and no status can be sent, for the same reason <c>RetryFilter</c> gives up on a started
/// response. A streaming handler should bound its own work.
/// </para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Assembly,
    AllowMultiple = false)]
[AnswersStatus(TimeoutPolicy.DefaultStatus, typeof(ErrorModel), StatusFrom = nameof(Status),
    Description = "The operation did not finish inside its budget.")]
public sealed class TimeoutAttribute : Attribute, IDeclaresTimeout {

    /// <summary>How long the operation may take before its token is cancelled.</summary>
    public int Milliseconds { get; set; } = TimeoutPolicy.DefaultMilliseconds;

    /// <summary>
    /// What a caller is told when the budget runs out. 504 by default; 503 where the operation is
    /// shedding load rather than waiting on something.
    /// </summary>
    public int Status { get; set; } = TimeoutPolicy.DefaultStatus;

    /// <summary>
    /// Seconds to put in <c>Retry-After</c>, or zero for no header. Only honest alongside
    /// <see cref="Status"/> 503.
    /// </summary>
    public int RetryAfterSeconds { get; set; }

    /// <inheritdoc />
    public TimeoutPolicy Timeout => new(Milliseconds, Status, RetryAfterSeconds);
}
