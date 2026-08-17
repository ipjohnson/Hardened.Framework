using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// The contributor that answers from the grants the credential itself carried.
/// </summary>
/// <remarks>
/// <para>
/// Registered by default so that <see cref="IActivityAuthorizationService"/> is useful with nothing
/// else set up - an application calling it directly to check a grant mid-handler gets a real answer
/// rather than an abstention. Applications add their own handlers alongside this one; it is
/// additive, not <c>Try</c>, because contributors are a set.
/// </para>
/// <para>
/// <b>It abstains rather than denying when a grant is missing</b>, and that distinction is the whole
/// reason contributors have three answers. "This grant is not in the credential" is not "this caller
/// does not have this grant" - the next handler along may resolve it from a permissions table, and a
/// deny here would outrank that and make every resolver useless.
/// </para>
/// </remarks>
[SingletonService]
public class PrincipalGrantAuthorizationHandler : IActivityAuthorizationHandler {
    private static readonly ValueTask<AuthorizationDecision> Allow =
        new(AuthorizationDecision.Allow);

    private static readonly ValueTask<AuthorizationDecision> Abstain =
        new(AuthorizationDecision.Abstain);

    public ValueTask<AuthorizationDecision> Authorize(
        IExecutionContext context, params string[] grants) {
        // Nothing was asked, so there is nothing to affirm. Reading this as "all zero grants are
        // held, therefore allow" would turn an empty question into a permit.
        if (grants.Length == 0) {
            return Abstain;
        }

        var held = context.CallerPrincipal.Grants;

        foreach (var grant in grants) {
            if (!held.Contains(grant)) {
                return Abstain;
            }
        }

        return Allow;
    }
}
