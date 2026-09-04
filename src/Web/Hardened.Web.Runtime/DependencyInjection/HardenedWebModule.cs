using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Conditional;
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

        // TryAddEnumerable rather than Add: a startup service registered twice runs twice, and this
        // one puts the CORS filter in the middleware chain - so a second registration is a second
        // filter on every request, with the "no allowed origins" notice logged beside it once per
        // copy. An application composing two web modules saw both.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStartupService, CorsStartupService>());

        // Always on, for every GET handler. A request carrying neither If-None-Match nor
        // If-Modified-Since costs two header lookups. TryAddEnumerable for the reason the CORS one
        // is: a second load of this module must not install a second copy, and the registry takes
        // every registered provider through its constructor.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRequestFilterProvider, ConditionalRequestProvider>());

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
