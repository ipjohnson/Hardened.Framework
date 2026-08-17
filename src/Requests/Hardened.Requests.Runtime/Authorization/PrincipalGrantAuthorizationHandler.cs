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
/// <b>It vouches for what it finds and says nothing about the rest.</b> A grant missing from the
/// credential is not a grant the caller lacks - the next handler along may resolve it from a
/// permissions table - so leaving it out of the set is the whole of this handler's answer, and a
/// refusal here would outrank that and make every resolver useless.
/// </para>
/// </remarks>
[SingletonService]
public class PrincipalGrantAuthorizationHandler : IActivityAuthorizationHandler {
    private static readonly ValueTask<GrantResolution> Abstained = new(GrantResolution.Abstained);

    public ValueTask<GrantResolution> Resolve(IExecutionContext context, IReadOnlyList<string> grants) {
        var held = context.CallerPrincipal.Grants;

        if (held.Count == 0 || grants.Count == 0) {
            return Abstained;
        }

        HashSet<string>? found = null;

        foreach (var grant in grants) {
            if (held.Contains(grant)) {
                (found ??= new HashSet<string>(StringComparer.Ordinal)).Add(grant);
            }
        }

        return found == null
            ? Abstained
            : new ValueTask<GrantResolution>(new GrantResolution(found));
    }
}
