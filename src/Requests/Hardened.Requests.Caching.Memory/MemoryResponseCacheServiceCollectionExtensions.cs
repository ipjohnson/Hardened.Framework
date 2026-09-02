using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Caching.Memory;

public static class MemoryResponseCacheServiceCollectionExtensions {

    /// <summary>
    /// Sets what the in-process response store will hold.
    /// </summary>
    /// <example>
    /// <code>
    /// public void ConfigureServices(IServiceCollection services) {
    ///     services.ConfigureMemoryResponseCache(cache => {
    ///         cache.SizeLimit = 32 * 1024 * 1024;
    ///         cache.MaximumBodySize = 1024 * 1024;
    ///     });
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// Here rather than on <c>[HardenedMemoryResponseCache]</c>, and that is a constraint rather
    /// than a preference - see the remarks on <see cref="HardenedMemoryResponseCache"/>. An amender
    /// rather than a replacement value, so this runs over whatever the module and the environment
    /// left rather than in place of it.
    /// </remarks>
    public static IServiceCollection ConfigureMemoryResponseCache(
        this IServiceCollection services, Action<MemoryResponseCacheConfiguration> configure) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                Array.Empty<IConfigurationValueProvider>(),
                new IConfigurationValueAmender[] {
                    new SimpleConfigurationValueAmender<MemoryResponseCacheConfiguration>(
                        (_, configuration) => {
                            configure(configuration);

                            return configuration;
                        })
                }));

        return services;
    }
}
