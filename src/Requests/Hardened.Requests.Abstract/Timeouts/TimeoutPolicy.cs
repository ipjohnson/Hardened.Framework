namespace Hardened.Requests.Abstract.Timeouts;

/// <summary>
/// How long an operation may take, and what its caller is told when it does not finish.
/// </summary>
/// <remarks>
/// <para>
/// One value rather than three, because all three cascade together. An assembly declaring
/// <c>[Timeout(Milliseconds = 2000, Status = 503, RetryAfterSeconds = 30)]</c> is stating one
/// policy about its handlers; splitting the budget from the answer would mean a handler could
/// inherit a budget from one rung and a status from another, and nothing would say which pairing
/// was intended.
/// </para>
/// <para>
/// Milliseconds rather than a <see cref="TimeSpan"/> because the declaration reaching this is
/// usually an attribute argument or an IDL member, and neither can carry one.
/// </para>
/// </remarks>
/// <param name="Milliseconds">
/// The budget. Validated where a declaration is read rather than here, so the failure can name the
/// handler and the rung it came from.
/// </param>
/// <param name="Status">
/// What the caller is told. 504 by default, which is what ASP.NET Core's request-timeout
/// middleware answers, and not 408 - that is a request that never finished arriving, which
/// <c>RequestTimeout</c>'s own remarks refuse for this.
/// </param>
/// <param name="RetryAfterSeconds">
/// Seconds to put in <c>Retry-After</c>, or zero for no header. Only honest alongside
/// <see cref="Status"/> 503: a deadline out at a dependency knows nothing about when that
/// dependency recovers.
/// </param>
public sealed record TimeoutPolicy(
    int Milliseconds,
    int Status = TimeoutPolicy.DefaultStatus,
    int RetryAfterSeconds = 0) {

    /// <summary>
    /// The budget an application-wide default takes when nothing states one. A bound rather than a
    /// target: the number worth writing is the one an operation's callers will actually wait.
    /// </summary>
    public const int DefaultMilliseconds = 30_000;

    public const int DefaultStatus = 504;

    /// <summary>
    /// The tighter of two policies, treating null as no bound at all.
    /// </summary>
    /// <remarks>
    /// How a convention's contribution is folded in. A convention can bound a handler that declared
    /// nothing and can shorten one that declared too much, and cannot lengthen one - the same rule
    /// <c>IAuthorizationConvention</c> follows, for the same reason: a convention standing between
    /// an unannotated handler and the world must not be defeatable by the handler, and must not
    /// quietly undo what the handler asked for either.
    /// </remarks>
    public static TimeoutPolicy? Tighter(TimeoutPolicy? left, TimeoutPolicy? right) {
        if (left == null) {
            return right;
        }

        if (right == null) {
            return left;
        }

        return right.Milliseconds < left.Milliseconds ? right : left;
    }
}
