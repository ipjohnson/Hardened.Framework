using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Caching;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Requests.Caching.Memory;

/// <summary>
/// Keeps this process's answers, so a handler declaring <c>[CacheResponse]</c> has somewhere to
/// put them.
///
/// <code>
/// [HardenedModule]
/// [HardenedWebModule]
/// [HardenedMemoryResponseCache]
/// [AspNetCoreRuntime]
/// public partial class Application { }
/// </code>
///
/// <para>
/// <b>A package rather than a flag, and that is the opt-in.</b> A store is a DI registration, and a
/// DI registration is exactly what a trimmer cannot remove - so registering one by default would
/// put the cache, and <c>Microsoft.Extensions.Caching.Memory</c> behind it, in every application
/// whether or not anything cached. Static content is a package for the same reason and says so in
/// the same words.
/// </para>
///
/// <para>
/// <b>What it does not do is eliminate the invoke.</b> On Lambda the request is still billed for
/// its duration; this is a downstream cache rather than a CDN. It is worth having when the
/// handler's work is a DynamoDB query or an external call, and worth nothing when the handler is
/// cheap.
/// </para>
/// </summary>
/// <remarks>
/// The size limit and the per-entry cap are set with
/// <see cref="MemoryResponseCacheServiceCollectionExtensions.ConfigureMemoryResponseCache"/> rather
/// than here. DependencyModules generates the module attribute by unwrapping
/// <c>Nullable&lt;T&gt;</c>, so a <c>long?</c> on a module becomes a <c>long</c> on the attribute
/// and the null guard it emits is always true - which would copy <c>0</c> onto the module for every
/// author who wrote the attribute with no arguments, and a size limit of zero stores nothing.
/// </remarks>
[DependencyModule]
public partial class HardenedMemoryResponseCache : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                new IConfigurationValueProvider[] {
                    new NewConfigurationValueProvider<IMemoryResponseCacheConfiguration,
                        MemoryResponseCacheConfiguration>(null)
                },
                Array.Empty<IConfigurationValueAmender>()));

        services.TryAddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IMemoryResponseCacheConfiguration>()));

        // Try, so an application or a test that has substituted a clock keeps it. The store reads
        // this to decide whether an entry is still valid, which is what makes a duration something
        // a test can move rather than something it has to wait out.
        services.TryAddSingleton(TimeProvider.System);

        // Try, so a deployment that has a shared store - Amz replaces this one registration -
        // wins over the in-process one whichever order the modules were listed in.
        services.TryAddSingleton<IResponseCacheStore, MemoryResponseCacheStore>();
    }
}
