using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
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
/// </remarks>
internal class CorsStartupService : IStartupService {

    public Task<bool> Startup(IServiceProvider rootProvider) {
        var config = rootProvider.GetRequiredService<CorsConfiguration>();
        var middleware = rootProvider.GetRequiredService<IMiddlewareService>();
        var filter = rootProvider.GetRequiredService<CorsFilter>();

        middleware.Use(_ => filter);

        if (!config.IsConfigured) {
            // Resolved rather than injected, and optional: a startup service that cannot be
            // constructed without a logging stack is one that breaks every minimal container for
            // the sake of a message.
            rootProvider.GetService<ILogger<CorsStartupService>>()?.LogInformation(
                "CORS is registered with no allowed origins, so every cross-origin request will be " +
                "refused. Set {EnvironmentVariable} or call AllowOrigin to configure it.",
                config.EnvironmentVariable);
        }

        return Task.FromResult(true);
    }
}
