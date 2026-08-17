using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Installs the authorization filter provider at startup.
/// </summary>
/// <remarks>
/// At startup rather than per request, because the provider is asked once per handler as its filter
/// chain is built and the answer is kept for the life of the application.
/// </remarks>
internal class AuthorizationStartupService : IStartupService {
    public Task<bool> Startup(IServiceProvider rootProvider) {
        var configuration = rootProvider
            .GetRequiredService<IOptions<IAuthorizationConfiguration>>().Value;

        var provider = new AuthorizationFilterProvider(configuration.RequireAuthorization);

        rootProvider.GetRequiredService<IGlobalFilterRegistry>().RegisterFilter(provider.GetFilter);

        return Task.FromResult(true);
    }
}
