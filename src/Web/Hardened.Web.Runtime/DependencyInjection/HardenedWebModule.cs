using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Cors;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.Health;
using Hardened.Web.Runtime.OpenApi;
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
                    new NewConfigurationValueProvider<IWebRoutingConfiguration, WebRoutingConfiguration>(null)
                }, Array.Empty<IConfigurationValueAmender>())
        );

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

        services.TryAddSingleton<HealthCheckConfiguration>();

        // The controllers the framework's own endpoints invoke through. Registered here rather than
        // beside each provider because InstanceFilter resolves a controller with GetRequiredService,
        // and OpenApiDocumentProvider is constructed by generated code with no module of its own to
        // register from.
        services.TryAddSingleton<OpenApiDocumentController>();
        services.TryAddSingleton<HealthCheckController>();

        // Registered ahead of anything an application adds, because providers are consulted in
        // reverse registration order - so an application declaring its own route at either health
        // path shadows this rather than colliding with it.
        services.AddSingleton<IWebExecutionRequestHandlerProvider>(
            serviceProvider => new HealthCheckProvider(
                serviceProvider.GetRequiredService<HealthCheckConfiguration>(), serviceProvider));
    }
}
