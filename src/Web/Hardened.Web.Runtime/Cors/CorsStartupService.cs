using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.Cors;

internal class CorsStartupService : IStartupService {
    public Task<bool> Startup(IServiceProvider rootProvider) {
        var config = rootProvider.GetRequiredService<CorsConfiguration>();

        if (config.AllowedOrigins.Count > 0) {
            var middleware = rootProvider.GetRequiredService<IMiddlewareService>();
            var filter = rootProvider.GetRequiredService<CorsFilter>();
            middleware.Use(_ => filter);
        }

        return Task.FromResult(true);
    }
}
