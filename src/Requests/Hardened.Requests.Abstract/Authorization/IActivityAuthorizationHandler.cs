using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// One contributor to the answer <see cref="IActivityAuthorizationService"/> composes.
/// </summary>
/// <remarks>
/// <para>
/// Several may be registered, each knowing about a different source of grants, and each is free to
/// have no opinion. A handler that does not recognise the operation, the tenant, or the grants being
/// asked about returns <see cref="AuthorizationDecision.Abstain"/> - it must not return
/// <see cref="AuthorizationDecision.Allow"/> to mean "not my problem", because allow is a decision
/// and abstain is the absence of one.
/// </para>
/// <para>
/// This is also the seam for grants that live in a store rather than in the token, which is what
/// makes the resource-server design independent of how tokens are issued.
/// </para>
/// </remarks>
public interface IActivityAuthorizationHandler {
    /// <summary>
    /// Says whether this handler's source of truth grants the caller <paramref name="grants"/>.
    /// </summary>
    ValueTask<AuthorizationDecision> Authorize(IExecutionContext context, params string[] grants);
}
