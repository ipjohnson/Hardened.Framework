using Hardened.Requests.Abstract.Caching;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Caching.Memory.Tests;

/// <summary>
/// What referencing the package and writing the module attribute actually registers.
/// </summary>
public class HardenedMemoryResponseCacheTests {

    private static IServiceProvider Application(Action<IServiceCollection>? configure = null) {
        var services = new ServiceCollection();

        services.AddSingleton<IHardenedEnvironment>(new EnvironmentImpl());
        services.AddSingleton<IConfigurationManager, ConfigurationManager>();

        new HardenedMemoryResponseCache().ConfigureServices(services);

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The whole opt-in. Without a store the filter has nowhere to put an answer, and says so on the
    /// first request rather than caching nothing quietly.
    /// </summary>
    [Fact]
    public void TheModuleRegistersAStore() {
        Assert.IsType<MemoryResponseCacheStore>(
            Application().GetService<IResponseCacheStore>());
    }

    /// <summary>
    /// Registered with <c>Try</c>, so a deployment that has a shared store - which is what Amz
    /// replaces this registration with - wins whichever order the modules were listed in.
    /// </summary>
    [Fact]
    public void AStoreAlreadyRegisteredIsNotReplaced() {
        var services = new ServiceCollection();
        var shared = new SharedStore();

        services.AddSingleton<IHardenedEnvironment>(new EnvironmentImpl());
        services.AddSingleton<IConfigurationManager, ConfigurationManager>();
        services.AddSingleton<IResponseCacheStore>(shared);

        new HardenedMemoryResponseCache().ConfigureServices(services);

        Assert.Same(shared, services.BuildServiceProvider().GetService<IResponseCacheStore>());
    }

    [Fact]
    public void TheDefaultLimitsAreWhatTheModuleRegisters() {
        var configuration = Application()
            .GetRequiredService<IConfigurationManager>()
            .GetConfiguration<IMemoryResponseCacheConfiguration>();

        Assert.Equal(MemoryResponseCacheConfiguration.DefaultSizeLimit, configuration.SizeLimit);
        Assert.Equal(
            MemoryResponseCacheConfiguration.DefaultMaximumBodySize, configuration.MaximumBodySize);
    }

    /// <summary>
    /// The limits are set here rather than on the module attribute, because DependencyModules
    /// unwraps <c>Nullable&lt;T&gt;</c> and would copy 0 onto the module for anyone who wrote the
    /// attribute with no arguments. This is the supported route, so it has to work.
    /// </summary>
    [Fact]
    public void ConfiguringTheCacheChangesWhatTheStoreIsBuiltWith() {
        var configuration = Application(services => services.ConfigureMemoryResponseCache(cache => {
                cache.SizeLimit = 4096;
                cache.MaximumBodySize = 512;
            }))
            .GetRequiredService<IConfigurationManager>()
            .GetConfiguration<IMemoryResponseCacheConfiguration>();

        Assert.Equal(4096, configuration.SizeLimit);
        Assert.Equal(512, configuration.MaximumBodySize);
    }

    /// <summary>
    /// An amender rather than a replacement value, so two calls both apply rather than the second
    /// discarding the first.
    /// </summary>
    [Fact]
    public void TwoConfigurationCallsBothApply() {
        var configuration = Application(services => {
                services.ConfigureMemoryResponseCache(cache => cache.SizeLimit = 4096);
                services.ConfigureMemoryResponseCache(cache => cache.MaximumBodySize = 512);
            })
            .GetRequiredService<IConfigurationManager>()
            .GetConfiguration<IMemoryResponseCacheConfiguration>();

        Assert.Equal(4096, configuration.SizeLimit);
        Assert.Equal(512, configuration.MaximumBodySize);
    }

    private sealed class SharedStore : IResponseCacheStore {
        public ValueTask<CachedResponse?> Get(string key, CancellationToken cancellationToken) =>
            new((CachedResponse?)null);

        public ValueTask Set(
            string key, CachedResponse response, TimeSpan duration, CancellationToken cancellationToken) =>
            default;

        public ValueTask EvictByTag(string tag, CancellationToken cancellationToken) => default;
    }
}
