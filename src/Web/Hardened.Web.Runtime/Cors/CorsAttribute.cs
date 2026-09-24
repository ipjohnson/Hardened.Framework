using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// Answers cross-origin requests on the routes it covers, with the application's
/// <see cref="CorsConfiguration"/>.
/// </summary>
/// <remarks>
/// <para>
/// Declared on the handler method, on its class, or on the <c>[HardenedModule]</c> class for every
/// handler compiled with it, and the nearest declaration wins. <see cref="CorsAttribute{TPolicy}"/>
/// answers with a policy of its own instead.
/// </para>
/// <para>
/// An application that declares none keeps CORS on every request, as it had before this attribute
/// existed. Once a route or the module declares one, the generated routing table registers
/// <see cref="CorsManifest"/>, and CORS covers only the routes a declaration reaches. A cross-origin
/// request to any other route is answered with no CORS headers, and a preflight for it is refused.
/// </para>
/// <para>
/// The middleware still answers every preflight, because a preflight is an <c>OPTIONS</c> that no
/// route declares. It reads this declaration off the route the preflight asks about.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CorsAttribute : Attribute, IRequestFilterProvider
{
    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
    {
        // A class's or a module's declaration under a nearer one is overridden rather than added
        // to, so only the nearest installs a filter.
        if (!ReferenceEquals(Nearest(handlerInfo.Metadata), this))
        {
            yield break;
        }

        CorsRouteFilter? filter = null;

        yield return new RequestFilterInfo(
            context => filter ??= new CorsRouteFilter(Policy(context.RequestServices)),
            FilterOrder.Before + FilterOrder.RateLimitTransport,
            nameof(CorsRouteFilter)
        );
    }

    /// <summary>The configuration this declaration answers with.</summary>
    internal virtual CorsConfiguration Policy(IServiceProvider services) =>
        services.GetRequiredService<CorsConfiguration>();

    /// <summary>
    /// The first declaration in <paramref name="declarations"/>. A handler's metadata lists its
    /// method's attributes ahead of its class's and its module's, so the first is the nearest.
    /// </summary>
    internal static CorsAttribute? Nearest(IReadOnlyList<object> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (declaration is CorsAttribute cors)
            {
                return cors;
            }
        }

        return null;
    }
}

/// <summary>
/// Answers cross-origin requests on the routes it covers, with the policy
/// <see cref="CorsServiceCollectionExtensions.AddCorsPolicy{TPolicy}"/> registered for
/// <typeparamref name="TPolicy"/>.
/// </summary>
/// <remarks>
/// A type rather than a name, so a misspelled policy does not compile. A policy that nothing
/// registered fails the route's first request with <see cref="InvalidOperationException"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CorsAttribute<TPolicy> : CorsAttribute
{
    internal override CorsConfiguration Policy(IServiceProvider services) =>
        services.GetService<CorsPolicy<TPolicy>>()?.Configuration
        ?? throw new InvalidOperationException(
            $"[Cors<{typeof(TPolicy).Name}>] names a policy that nothing registered. Register it "
                + $"with AddCorsPolicy<{typeof(TPolicy).Name}>."
        );
}
