using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Asks every registered handler and folds what they say into one decision.
/// </summary>
/// <remarks>
/// <para>
/// Handlers are resolved from the request's services rather than captured once, so a handler scoped
/// to the request - one holding a tenant, a unit of work, a connection - works without this type
/// knowing anything about lifetimes.
/// </para>
/// <para>
/// <c>Try</c>, so an application with a different idea of how contributors compose can register its
/// own. The composition rule itself lives in
/// <see cref="AuthorizationDecisions"/> and is not reimplemented here.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class ActivityAuthorizationService : IActivityAuthorizationService {
    public async ValueTask<AuthorizationDecision> Authorize(
        IExecutionContext context, params string[] grants) {
        var decision = AuthorizationDecision.Abstain;

        foreach (var handler in context.RequestServices.GetServices<IActivityAuthorizationHandler>()) {
            decision = AuthorizationDecisions.Combine(decision, await handler.Authorize(context, grants));

            // Nothing outranks a deny, and a handler behind this one may be a database round trip or
            // a call to an entitlement service. There is no answer left that would change the result.
            if (decision == AuthorizationDecision.Deny) {
                break;
            }
        }

        return decision;
    }
}
