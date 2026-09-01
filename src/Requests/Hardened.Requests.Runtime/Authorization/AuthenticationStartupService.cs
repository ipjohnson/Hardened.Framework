using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Installs the authentication middleware at startup, when anything registered a source.
/// </summary>
/// <remarks>
/// The sources are resolved once and the middleware instance is shared: a source establishes a
/// caller from a request, so it holds no per-request state, and resolving per request would put
/// a container lookup on every request of every application that opted in. An application that
/// registered no source gets no middleware, which is what keeps the anonymous default free.
/// </remarks>
internal class AuthenticationStartupService : IStartupService {
    public Task<bool> Startup(IServiceProvider rootProvider) {
        var sources = rootProvider.GetServices<IPrincipalSource>().ToArray();

        if (sources.Length > 0) {
            var middleware = new AuthenticationMiddleware(sources);

            rootProvider.GetRequiredService<IMiddlewareService>().Use(_ => middleware);
        }

        return Task.FromResult(true);
    }
}
