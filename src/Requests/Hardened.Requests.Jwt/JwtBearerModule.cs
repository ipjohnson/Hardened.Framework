using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Jwt;

/// <summary>
/// The configuration, the key source, the validator and the startup check. Loaded by
/// <see cref="JwtBearerAuthenticationAttribute{TScheme}"/>, which is what an application writes.
/// </summary>
/// <remarks>
/// <c>TryAdd</c> for the key source and the validator, so the testing package and an application
/// can put their own in place.
/// </remarks>
[HardenedModule]
public partial class JwtBearerModule : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<IJwtSigningKeySource>(provider => new JwksSigningKeySource(
            provider.GetRequiredService<IOptions<IJwtBearerConfiguration>>().Value
        ));
        services.TryAddSingleton<JwtBearerValidator>();
        services.AddSingleton<IStartupService, JwtBearerStartupService>();
    }
}
