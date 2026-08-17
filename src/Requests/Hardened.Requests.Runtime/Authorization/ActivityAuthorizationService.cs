using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Asks every registered handler once and merges what they say.
/// </summary>
/// <remarks>
/// <para>
/// Handlers are resolved from the request's services rather than captured once, so a handler scoped
/// to the request - one holding a tenant, a unit of work, a connection - works without this type
/// knowing anything about lifetimes.
/// </para>
/// <para>
/// <c>Try</c>, so an application with a different idea of how contributors compose can register its
/// own. The composition rules themselves live in <see cref="AuthorizationDecisions"/> and
/// <see cref="GrantResolution.Combine"/> and are not reimplemented here.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class ActivityAuthorizationService : IActivityAuthorizationService {
    public async ValueTask<GrantResolution> Resolve(
        IExecutionContext context, IReadOnlyList<string> grants) {
        var resolution = GrantResolution.Abstained;

        if (grants.Count == 0) {
            return resolution;
        }

        foreach (var handler in context.RequestServices.GetServices<IActivityAuthorizationHandler>()) {
            resolution = GrantResolution.Combine(resolution, await handler.Resolve(context, grants));

            // Nothing outranks a deny, and a handler behind this one may be a database round trip or
            // a call to an entitlement service. No answer it could give would change the result.
            if (resolution.Decision == AuthorizationDecision.Deny) {
                break;
            }
        }

        return resolution;
    }

    public async ValueTask<AuthorizationDecision> Authorize(
        IExecutionContext context, params string[] grants) {
        // Nothing was asked, so there is nothing to affirm. Reading an empty question as "all zero
        // grants are held, therefore allow" would turn it into a permit.
        if (grants.Length == 0) {
            return AuthorizationDecision.Abstain;
        }

        var resolution = await Resolve(context, grants);

        // A verdict stands whatever the grants say.
        if (resolution.Decision != AuthorizationDecision.Abstain) {
            return resolution.Decision;
        }

        foreach (var grant in grants) {
            if (!resolution.Granted.Contains(grant)) {
                // Abstain rather than deny: nobody said no, they simply did not vouch for all of it.
                return AuthorizationDecision.Abstain;
            }
        }

        return AuthorizationDecision.Allow;
    }
}
