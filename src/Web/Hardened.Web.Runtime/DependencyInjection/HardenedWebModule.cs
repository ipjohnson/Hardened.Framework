using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Configuration;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Web.Runtime.Compression;
using Hardened.Web.Runtime.Cors;
using Hardened.Web.Runtime.Links;
using Hardened.Requests.Abstract.Execution;
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
        // Web routing published as the application's dispatch, so a host can install it without
        // referencing this package. WebExecutionHandlerService registers against
        // IWebExecutionHandlerService and dependency injection resolves exact types, so an
        // IHandlerDispatch lookup would otherwise find nothing however many interfaces it derives
        // from. Resolved through the existing registration rather than added a second time, so
        // there is still one instance of it.
        services.AddSingleton<IHandlerDispatch>(
            serviceProvider => serviceProvider.GetRequiredService<IWebExecutionHandlerService>());

        // Compression and links joined routing here when they left HardenedRequestModule. Both are
        // HTTP: a Content-Encoding to negotiate and a Link header to write, neither of which a
        // Lambda invocation has anywhere to put. Response caching did not join them - a direct
        // invoke has a caller reading the answer, so caching one is meaningful and its filter stays
        // where every host can reach it.
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                new IConfigurationValueProvider[] {
                    new NewConfigurationValueProvider<IWebRoutingConfiguration, WebRoutingConfiguration>(null),
                    new NewConfigurationValueProvider<ILinkConfiguration, LinkConfiguration>(null),
                    new NewConfigurationValueProvider<ICompressionConfiguration, CompressionConfiguration>(null)
                }, Array.Empty<IConfigurationValueAmender>())
        );

        services.TryAddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IWebRoutingConfiguration>()));

        services.AddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<ILinkConfiguration>()));

        services.AddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<ICompressionConfiguration>()));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRequestFilterProvider, RequestDecompressionProvider>());

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
