using System.Diagnostics.CodeAnalysis;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Installs the authentication middleware at startup, when anything registered a source.
/// </summary>
/// <remarks>
/// <para>
/// The sources are resolved once and the middleware instance is shared: a source establishes a
/// caller from a request, so it holds no per-request state, and resolving per request would put
/// a container lookup on every request of every application that opted in. An application that
/// registered no source gets no middleware, which is what keeps the anonymous default free.
/// </para>
/// <para>
/// <b>Both interfaces, which is why the collection is held.</b> A registration attribute registers
/// a class as the interface it declares, so a source written against
/// <see cref="IPrincipalSource{TScheme}"/> sits in the container under the closed generic and
/// under nothing else. Resolving <see cref="IPrincipalSource"/> alone found none of them,
/// installed no middleware, and left every caller anonymous with nothing said - the typed form's
/// own remarks promise it works identically, and this is what makes that true. The service types
/// cannot be reached from a built provider, so the collection the module configured is read here
/// instead, once, after everything has registered into it.
/// </para>
/// </remarks>
internal class AuthenticationStartupService : IStartupService {
    private readonly IServiceCollection _services;

    public AuthenticationStartupService(IServiceCollection services) {
        _services = services;
    }

    public Task<bool> Startup(IServiceProvider rootProvider) {
        var sources = Sources(rootProvider);

        if (sources.Count > 0) {
            var middleware = new AuthenticationMiddleware(sources);

            rootProvider.GetRequiredService<IMiddlewareService>().Use(_ => middleware);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Every registered source, in registration order, whichever interface it was registered as.
    /// </summary>
    /// <remarks>
    /// A source registered under both interfaces is one source: the middleware asks each in turn
    /// until one answers, and asking the same instance twice would only cost the request a second
    /// read of a credential it already declined.
    /// </remarks>
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Every service type read here is an interface, so the IEnumerable<T> the " +
                        "container builds shares the canonical reference-type instantiation and " +
                        "needs no native code of its own. The closed types are not constructed - " +
                        "they are read from registrations the application already made.")]
    private List<IPrincipalSource> Sources(IServiceProvider rootProvider) {
        var sources = new List<IPrincipalSource>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var serviceType in SourceServiceTypes()) {
            foreach (var resolved in rootProvider.GetServices(serviceType)) {
                if (resolved is IPrincipalSource source && seen.Add(source)) {
                    sources.Add(source);
                }
            }
        }

        return sources;
    }

    /// <summary>
    /// The service types a principal source can be registered under, in the order they were
    /// registered: <see cref="IPrincipalSource"/> itself, and every closed
    /// <see cref="IPrincipalSource{TScheme}"/> anything asked for.
    /// </summary>
    /// <remarks>
    /// An open generic is skipped rather than resolved. Nothing can implement
    /// <c>IPrincipalSource&lt;TScheme&gt;</c> for every scheme, and asking the container for an
    /// unbound type throws.
    /// </remarks>
    private IEnumerable<Type> SourceServiceTypes() {
        var seen = new HashSet<Type>();

        foreach (var descriptor in _services) {
            var serviceType = descriptor.ServiceType;

            if (serviceType.ContainsGenericParameters) {
                continue;
            }

            if (serviceType != typeof(IPrincipalSource) &&
                !(serviceType.IsGenericType &&
                  serviceType.GetGenericTypeDefinition() == typeof(IPrincipalSource<>))) {
                continue;
            }

            if (seen.Add(serviceType)) {
                yield return serviceType;
            }
        }
    }
}
