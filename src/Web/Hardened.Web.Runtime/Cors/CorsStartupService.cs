using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.Cors;

internal class CorsStartupService : IStartupService {
    public Task<bool> Startup(IServiceProvider rootProvider) {
        var config = rootProvider.GetRequiredService<CorsConfiguration>();
        var filter = rootProvider.GetRequiredService<CorsFilter>();
        var registry = rootProvider.GetRequiredService<IGlobalFilterRegistry>();

        if (config.AllowedOrigins.Count > 0) {
            registry.RegisterFilter(filter, (int)ExecutionFilterOrder.Init + 1);
        }

        return Task.FromResult(true);
    }
}
