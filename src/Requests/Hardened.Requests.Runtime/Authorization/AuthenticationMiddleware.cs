using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Runs the registered principal sources and puts the answer on the context.
/// </summary>
/// <remarks>
/// <para>
/// Middleware rather than a request filter, so it runs ahead of the whole handler chain and both
/// authorization positions see the same principal - the arrangement every test fixture that stood
/// in for authentication already used, now shipped.
/// </para>
/// <para>
/// First answer wins, in registration order. Sources are disjoint by construction - each reads
/// its own credential - so ordering only decides ties on a request carrying two credentials, and
/// registration order is the order the application stated. A request no source answers for is
/// left exactly as it started: <see cref="AnonymousCallerPrincipal.Instance"/>, judged by
/// authorization the same way it was before this middleware existed.
/// </para>
/// <para>
/// Installed by <see cref="AuthenticationStartupService"/> only when at least one source is
/// registered, so an application using none carries no per-request cost at all.
/// </para>
/// <para>
/// The answer is put on the request scope as well as on the context, which is what makes
/// <see cref="ICurrentCaller"/> resolvable in a specification-first handler - one whose signature a
/// generated interface fixes, so it cannot take the context as a parameter. Written from the
/// context rather than from the source's return value, so a request no source answered for reads
/// whatever the context holds.
/// </para>
/// </remarks>
public sealed class AuthenticationMiddleware : IExecutionFilter {
    private readonly IPrincipalSource[] _sources;

    public AuthenticationMiddleware(IEnumerable<IPrincipalSource> sources) {
        _sources = sources.ToArray();
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        for (var i = 0; i < _sources.Length; i++) {
            var principal = await _sources[i].Authenticate(context);

            if (principal != null) {
                context.CallerPrincipal = principal;

                break;
            }
        }

        // One resolve per request of an application that opted into authentication. Asked for
        // rather than required, because a host that composed this middleware without the request
        // module's registrations still has a caller to establish.
        if (context.RequestServices?.GetService<CurrentCaller>() is { } caller) {
            caller.Principal = context.CallerPrincipal;
        }

        await chain.Next();
    }
}
