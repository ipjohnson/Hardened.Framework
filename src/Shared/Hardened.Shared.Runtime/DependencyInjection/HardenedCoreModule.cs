using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;

namespace Hardened.Shared.Runtime.DependencyInjection;

[DependencyModule]
public partial class HardenedCoreModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
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

        RegisterTimeProvider(services);
    }

    /// <summary>
    /// The clock, as the BCL's own abstraction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every arm of the last two trials hand-rolled an <c>IClock</c> and a <c>SystemClock</c> to
    /// drive expiry from a test, and every one of them was the same ten lines. <c>TimeProvider</c>
    /// is what .NET 8 added for it, so there is nothing here to invent: an application injects
    /// <c>TimeProvider</c> and a test substitutes it with <c>[Mock]</c> like any other singleton.
    /// </para>
    /// <para>
    /// <c>TryAdd</c>, so an application that registers its own - a fake, or one of
    /// <c>Microsoft.Extensions.TimeProvider.Testing</c>'s - keeps it. The framework reads no time
    /// through this; it is here because every application needs one and none should have to write
    /// it.
    /// </para>
    /// </remarks>
    private static void RegisterTimeProvider(IServiceCollection services) {
        services.TryAddSingleton(TimeProvider.System);
    }
}
