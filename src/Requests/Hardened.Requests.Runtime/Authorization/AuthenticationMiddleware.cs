using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

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

        await chain.Next();
    }
}
