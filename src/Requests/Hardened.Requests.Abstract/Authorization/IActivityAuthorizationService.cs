using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// What does this <b>caller</b> hold? The other half of an authorization decision.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAuthorizationPolicy"/> answers what the <em>operation</em> requires; this answers what
/// the <em>caller</em> has. Most requirements need neither - a grant carried in the credential is
/// read straight off <see cref="ICallerPrincipal.Grants"/>, synchronously, with nothing to resolve.
/// </para>
/// <para>
/// This exists for the grants that are not in the credential: a permissions table, a per-tenant role
/// expansion, an entitlement service. That is why it is asynchronous, and why it composes several
/// <see cref="IActivityAuthorizationHandler"/>s rather than being one answer.
/// </para>
/// </remarks>
public interface IActivityAuthorizationService {
    /// <summary>
    /// Asks every registered handler which of <paramref name="grants"/> the caller holds, and merges
    /// what they say.
    /// </summary>
    /// <remarks>
    /// One call for the whole list. The grants union across handlers, because each knows about a
    /// different source; the verdicts compose by the usual rule, so one refusal still outranks
    /// everything.
    /// </remarks>
    ValueTask<GrantResolution> Resolve(IExecutionContext context, IReadOnlyList<string> grants);

    /// <summary>
    /// Whether the caller holds every one of <paramref name="grants"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The imperative form, for a handler checking a grant in its own body rather than declaring one
    /// on its signature. Derived from <see cref="Resolve"/> rather than asked separately, so the two
    /// cannot disagree.
    /// </para>
    /// <para>
    /// A conjunction: this is the question "may the caller do this specific thing", and every grant
    /// named is part of it. Asking about alternatives means calling <see cref="Resolve"/> and
    /// deciding, which is what the pipeline does with a requirement that has structure.
    /// </para>
    /// </remarks>
    ValueTask<AuthorizationDecision> Authorize(IExecutionContext context, params string[] grants);
}
