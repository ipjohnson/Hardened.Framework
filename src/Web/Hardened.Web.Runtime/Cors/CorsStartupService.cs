using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// Puts the CORS filter in the middleware chain.
/// </summary>
/// <remarks>
/// <para>
/// Registered unconditionally. It used to be registered only when origins had already been
/// configured, which combined badly with the configuration being loaded from an environment
/// variable inside the DI factory: a misspelled <c>CORS_ALLOWED_ORIGINS</c> meant not "CORS allows
/// nothing" but "CORS does not exist", and the two look identical from the outside until a browser
/// says otherwise.
/// </para>
/// <para>
/// Registering always costs one dictionary lookup on requests with no <c>Origin</c> header, which
/// is the price of the failure being visible.
/// </para>
/// <para>
/// Where the build registered <see cref="CorsManifest"/>, routes declare their own CORS. The filter
/// installed then answers only preflights, and every other request leaves it after one method
/// comparison.
/// </para>
/// </remarks>
internal class CorsStartupService : IStartupService
{
    public Task<bool> Startup(IServiceProvider rootProvider)
    {
        var config = rootProvider.GetRequiredService<CorsConfiguration>();
        var middleware = rootProvider.GetRequiredService<IMiddlewareService>();
        var routesDeclare = rootProvider.GetService<CorsManifest>() != null;

        var filter = routesDeclare
            ? new CorsFilter(
                config,
                rootProvider.GetService<IEnumerable<IWebExecutionRequestHandlerProvider>>(),
                new RouteCorsPolicies(rootProvider)
            )
            : rootProvider.GetRequiredService<CorsFilter>();

        middleware.Use(_ => filter);

        // Only where the configuration covers the whole application. Once routes declare CORS, a
        // route may name a policy of its own, and an empty default refuses nothing it is asked for.
        if (!routesDeclare && !config.IsConfigured)
        {
            // Resolved rather than injected, and optional: a startup service that cannot be
            // constructed without a logging stack is one that breaks every minimal container for
            // the sake of a message.
            rootProvider
                .GetService<ILogger<CorsStartupService>>()
                ?.LogInformation(
                    "CORS is registered with no allowed origins, so every cross-origin request will be "
                        + "refused. Set {EnvironmentVariable} or call AllowOrigin to configure it.",
                    config.EnvironmentVariable
                );
        }

        return Task.FromResult(true);
    }
}
