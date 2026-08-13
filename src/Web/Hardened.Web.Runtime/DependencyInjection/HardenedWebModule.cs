using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Templates.Runtime.DependencyInjection;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Cors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Runtime.DependencyInjection;

/// <summary>
/// <c>HardenedTemplateModule</c> is declared here because the template generator emits a
/// <c>TemplateExecutionHandlerProvider</c> into every entry point it sees — unconditionally, with
/// no templates required — and that provider takes an <c>IInternalTemplateServices</c> which only
/// the template module registers. Without it the container holds a singleton it cannot construct.
///
/// Nothing resolves that provider unless a route renders a template, so the gap stayed invisible:
/// the xUnit harness builds its provider without validation, and so does the Kestrel host. It
/// surfaces only where the container is validated eagerly — <c>WebApplication.Build()</c> in the
/// Development environment turns on <c>ValidateOnBuild</c>, walks every descriptor, and throws
/// before the first request. Every Hardened web application was affected.
///
/// This costs nothing to declare: Hardened.Web.Runtime already references
/// Hardened.Templates.Runtime, so the assembly was loaded either way.
/// </summary>
[DependencyModule]
[HardenedRequestModule]
[HardenedTemplateModule]
public partial class HardenedWebModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                new[] {
                    new NewConfigurationValueProvider<IStaticContentConfiguration, StaticContentConfiguration>(null)
                }, Array.Empty<IConfigurationValueAmender>())
        );
        services.TryAddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IStaticContentConfiguration>()));

        services.AddSingleton<CorsConfiguration>(sp => {
            var config = new CorsConfiguration();
            config.LoadFromEnvironment();
            return config;
        });
        services.AddSingleton<CorsFilter>();
        services.AddSingleton<IStartupService, CorsStartupService>();
    }
}
