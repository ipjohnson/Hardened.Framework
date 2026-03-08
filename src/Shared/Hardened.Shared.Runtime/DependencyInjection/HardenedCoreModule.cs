using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;

namespace Hardened.Shared.Runtime.DependencyInjection;

[DependencyModule]
public partial class HardenedCoreModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<IStringBuilderPool, StringBuilderPool>();
        services.TryAddSingleton<IMemoryStreamPool, MemoryStreamPool>();
        services.TryAddSingleton<IConfigurationManager, ConfigurationManager>();
        services.TryAddSingleton<IMetricLoggerProvider, NullMetricLoggerProvider>();
        services.TryAddSingleton<IFileExtToMimeTypeHelper, FileExtToMimeTypeHelper>();
        services.TryAddSingleton<IJsonSerializer, JsonSerializerImpl>();
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                new[] {
                    new NewConfigurationValueProvider<IJsonSerializerConfiguration,
                        JsonSerializerConfiguration>(null)
                }, Array.Empty<IConfigurationValueAmender>())
        );
        services.TryAddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IJsonSerializerConfiguration>()));

        services.TryAddSingleton<IItemPool<MD5>>(_ =>
            new ItemPool<MD5>(MD5.Create, _ => { }, md5 => md5.Dispose()));
    }
}
