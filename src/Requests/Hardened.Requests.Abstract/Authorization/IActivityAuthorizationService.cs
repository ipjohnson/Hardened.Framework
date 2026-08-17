using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// Does this <b>caller</b> hold these grants? The other half of an authorization decision.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAuthorizationPolicy"/> answers what the <em>operation</em> requires; this answers
/// what the <em>caller</em> has. Most requirements need neither - a grant carried in the credential
/// is read straight off <see cref="ICallerPrincipal.Grants"/>, synchronously, with nothing to
/// resolve.
/// </para>
/// <para>
/// This exists for the grants that are not in the credential: a permissions table, a per-tenant role
/// expansion, an entitlement service. That is why it is asynchronous, and why it composes the
/// answers of several <see cref="IActivityAuthorizationHandler"/>s rather than being one answer.
/// </para>
/// </remarks>
public interface IActivityAuthorizationService {
    /// <summary>
    /// Asks every registered handler whether the caller holds <paramref name="grants"/>, and folds
    /// what they say into one decision.
    /// </summary>
    /// <remarks>
    /// The fold is <see cref="AuthorizationDecisions.Combine(IEnumerable{AuthorizationDecision})"/>,
    /// and an all-abstaining or empty set of handlers yields
    /// <see cref="AuthorizationDecision.Abstain"/>, which does not permit.
    /// </remarks>
    ValueTask<AuthorizationDecision> Authorize(IExecutionContext context, params string[] grants);
}
