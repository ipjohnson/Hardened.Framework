using Newtonsoft.Json;
using Xunit;

namespace Hardened.Requests.Serializers.Newtonsoft.Tests;

/// <summary>
/// The configuration model the package exposes.
/// </summary>
/// <remarks>
/// Generated from <c>[ConfigurationModel]</c>, so what is being checked is that the generator gave
/// the field a settable property and a working default — the shape a consumer writes against. The
/// default has to produce a serializer without any configuration at all, because installing the
/// package and configuring nothing is the common case.
/// </remarks>
public class NewtonsoftSerializerConfigurationTests {

    [Fact]
    public void TheDefaultProviderBuildsASerializer() {
        var configuration = new NewtonsoftSerializerConfiguration();

        Assert.NotNull(configuration.SerializerProvider(null!));
    }

    /// <summary>
    /// A fresh serializer per call rather than a shared one. <c>SharedSerializer</c> is what makes
    /// it a singleton, and it calls this exactly once.
    /// </summary>
    [Fact]
    public void TheDefaultProviderBuildsAFreshSerializerEachCall() {
        var configuration = new NewtonsoftSerializerConfiguration();

        Assert.NotSame(
            configuration.SerializerProvider(null!), configuration.SerializerProvider(null!));
    }

    [Fact]
    public void TheProviderCanBeReplaced() {
        var expected = JsonSerializer.CreateDefault();
        var configuration = new NewtonsoftSerializerConfiguration {
            SerializerProvider = _ => expected
        };

        Assert.Same(expected, configuration.SerializerProvider(null!));
    }

    [Fact]
    public void TheConfigurationImplementsItsGeneratedInterface() {
        Assert.IsAssignableFrom<INewtonsoftSerializerConfiguration>(
            new NewtonsoftSerializerConfiguration());
    }
}
