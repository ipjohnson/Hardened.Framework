using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.DependencyInjection;

[DependencyModule]
[HardenedCoreModule]
public partial class HardenedRequestModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(new IConfigurationValueProvider[] {
                new NewConfigurationValueProvider<IResponseHeaderConfiguration, ResponseHeaderConfiguration>(null),
                new NewConfigurationValueProvider<IJsonSerializerConfiguration, JsonSerializerConfiguration>(null)
            }));
        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IResponseHeaderConfiguration>()));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IJsonSerializerConfiguration>()));
    }
}
