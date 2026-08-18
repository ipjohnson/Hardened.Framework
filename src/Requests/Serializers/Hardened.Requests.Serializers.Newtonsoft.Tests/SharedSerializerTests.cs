using Hardened.Requests.Serializers.Newtonsoft.Impl;
using Hardened.Requests.Serializers.Newtonsoft.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace Hardened.Requests.Serializers.Newtonsoft.Tests;

/// <summary>
/// The one <see cref="JsonSerializer"/> both directions share.
/// </summary>
/// <remarks>
/// A single instance is the point: settings configured once must apply to reading and writing
/// alike, or a model that round-trips under one naming strategy is written under another. It is
/// registered as a singleton and built from the configured provider exactly once, which is also
/// what makes an expensive <c>ContractResolver</c> affordable — Newtonsoft caches contracts on the
/// resolver, and a per-request serializer would rebuild them.
/// </remarks>
public class SharedSerializerTests {

    private static SharedSerializer Build(
        Func<IServiceProvider, JsonSerializer>? provider = null,
        Action<ServiceCollection>? configureServices = null) {
        var services = new ServiceCollection();

        configureServices?.Invoke(services);

        return new SharedSerializer(services.BuildServiceProvider(), Pipeline.Configuration(provider));
    }

    [Fact]
    public void TheDefaultConfigurationYieldsASerializer() {
        Assert.NotNull(Build().Serializer);
    }

    [Fact]
    public void TheConfiguredProviderIsWhatBuildsIt() {
        var expected = JsonSerializer.CreateDefault();

        Assert.Same(expected, Build(_ => expected).Serializer);
    }

    /// <summary>
    /// Built once, in the constructor. A provider called per access would rebuild the contract
    /// cache on every request.
    /// </summary>
    [Fact]
    public void TheProviderRunsOnceRatherThanPerAccess() {
        var calls = 0;

        var shared = Build(_ => {
            calls++;

            return JsonSerializer.CreateDefault();
        });

        _ = shared.Serializer;
        _ = shared.Serializer;

        Assert.Equal(1, calls);
    }

    [Fact]
    public void TheSerializerIsTheSameInstanceEveryTime() {
        var shared = Build();

        Assert.Same(shared.Serializer, shared.Serializer);
    }

    /// <summary>
    /// The provider is handed the application's container, so a serializer can be built from
    /// registered services — a converter that needs a clock or a tenant, for instance.
    /// </summary>
    [Fact]
    public void TheProviderIsGivenTheServiceProvider() {
        IServiceProvider? seen = null;

        Build(
            serviceProvider => {
                seen = serviceProvider;

                return JsonSerializer.CreateDefault();
            },
            services => services.AddSingleton("registered"));

        Assert.NotNull(seen);
        Assert.Equal("registered", seen.GetRequiredService<string>());
    }

    /// <summary>
    /// Reading and writing share one instance, so settings configured once apply to both.
    /// </summary>
    [Fact]
    public void BothDirectionsSeeTheSameSerializer() {
        var shared = Build();
        var pool = Pipeline.Pool();

        var deserializer = new NewtonsoftDeserializer(
            pool, shared, Microsoft.Extensions.Logging.Abstractions.NullLogger<NewtonsoftDeserializer>.Instance);
        var serializer = new NewtonsoftSerializer(shared, pool);

        Assert.NotNull(deserializer);
        Assert.NotNull(serializer);
        Assert.Same(shared.Serializer, shared.Serializer);
    }
}
