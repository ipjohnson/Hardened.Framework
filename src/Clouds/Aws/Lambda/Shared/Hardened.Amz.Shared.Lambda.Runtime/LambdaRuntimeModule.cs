using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Shared.Lambda.Runtime;

[DependencyModule]
public partial class LambdaRuntimeModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        // Registered the way the framework registers its own configuration types, so an application
        // amends it through IAppConfig like any other and a filter reads it through IOptions.
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(new IConfigurationValueProvider[] {
                new NewConfigurationValueProvider<ILambdaResponseModeConfiguration, LambdaResponseModeConfiguration>(
                    LambdaResponseModeConfiguration.FromEnvironment)
            }));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<ILambdaResponseModeConfiguration>()));
    }
}
