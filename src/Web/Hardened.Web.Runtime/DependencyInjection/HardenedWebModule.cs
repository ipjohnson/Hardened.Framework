using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Cors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Runtime.DependencyInjection;

[DependencyModule]
[HardenedRequestModule]
public partial class HardenedWebModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                new IConfigurationValueProvider[] {
                    new NewConfigurationValueProvider<IStaticContentConfiguration, StaticContentConfiguration>(null),
                    new NewConfigurationValueProvider<IWebRoutingConfiguration, WebRoutingConfiguration>(null)
                }, Array.Empty<IConfigurationValueAmender>())
        );
        services.TryAddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IStaticContentConfiguration>()));

        services.TryAddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IWebRoutingConfiguration>()));

        services.AddSingleton<CorsConfiguration>(sp => {
            var config = new CorsConfiguration();
            config.LoadFromEnvironment();
            return config;
        });
        services.AddSingleton<CorsFilter>();
        services.AddSingleton<IStartupService, CorsStartupService>();
    }
}
