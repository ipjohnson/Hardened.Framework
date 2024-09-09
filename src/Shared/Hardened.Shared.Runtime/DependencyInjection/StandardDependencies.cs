using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Hardened.Shared.Runtime.DependencyInjection;

public sealed class StandardDependencies {
    public static void ProcessModuleProviders(
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection,
        params object[] otherModules) {
        
        foreach (var objectModule in otherModules) {
            if (objectModule is not IApplicationModuleProvider provider) {
                continue;
            }

            foreach (var applicationModule in provider.ProvideModules()) {
                applicationModule.ConfigureModule(environment, serviceCollection);
            }
        }
    }
    
    public static void ProcessModules(
        IHardenedEnvironment environment, 
        IServiceCollection serviceCollection,
        IEnumerable<IApplicationModule> applicationModules) {
        
        foreach (var applicationModule in applicationModules) {
            applicationModule.ConfigureModule(environment, serviceCollection);
        }
    }

    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        if (DependencyRegistry<StandardDependencies>.ShouldRegisterModule(serviceCollection)) {

            serviceCollection.TryAddSingleton<IStringBuilderPool, StringBuilderPool>();
            serviceCollection.TryAddSingleton<IMemoryStreamPool, MemoryStreamPool>();
            serviceCollection.TryAddSingleton<IConfigurationManager, ConfigurationManager>();
            serviceCollection.TryAddSingleton<IMetricLoggerProvider, NullMetricLoggerProvider>();
            serviceCollection.TryAddSingleton<IFileExtToMimeTypeHelper, FileExtToMimeTypeHelper>();
            serviceCollection.TryAddSingleton<IJsonSerializer, JsonSerializerImpl>();
            serviceCollection.AddSingleton<IConfigurationPackage>(
                new SimpleConfigurationPackage(
                    new[] {
                        new NewConfigurationValueProvider<IJsonSerializerConfiguration,
                            JsonSerializerConfiguration>(null)
                    }, Array.Empty<IConfigurationValueAmender>())
            );
            serviceCollection.TryAddSingleton(
                serviceProvider => Microsoft.Extensions.Options.Options.Create(
                    serviceProvider.GetRequiredService<IConfigurationManager>()
                        .GetConfiguration<IJsonSerializerConfiguration>()));

            serviceCollection.TryAddSingleton<IItemPool<MD5>>(_ =>
                new ItemPool<MD5>(MD5.Create, _ => { }, md5 => md5.Dispose()));
        }
    }
}