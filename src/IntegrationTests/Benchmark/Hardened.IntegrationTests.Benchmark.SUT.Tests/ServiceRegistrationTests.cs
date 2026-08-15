using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.Benchmark.SUT.Tests;

/// <summary>
/// That the services this application depends on are actually in the container.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a failure that produced no error. <c>TemplateResponseSerializer</c> was
/// registered with <c>RegistrationType.Try</c>, which emits <c>TryAddSingleton</c> - first-wins per
/// service type. On <c>IResponseSerializer</c>, an interface with several implementations, that
/// means "do not register if anyone else already did", so the JSON serializer got there first and
/// the template serializer never entered the container at all. <c>/fortunes</c> answered with a
/// JSON-serialized model, the build was green, and nothing anywhere reported a problem: a no-op
/// registration is not an error.
/// </para>
/// <para>
/// Route tests catch the symptom. These catch the cause, and would catch it again for any service
/// resolved as a set that someone registers with <c>Try</c>.
/// </para>
/// <para>
/// There is nothing template-specific left to register, and no template serializer either. An
/// output writes the response itself, and a handler declaring one takes it out of negotiation
/// entirely - so the engine, the template source, the name-keyed registry and the serializer that
/// ordered them against JSON are all gone.
/// </para>
/// </remarks>
public class ServiceRegistrationTests {

    private static List<string> RegistrationsFor(Type serviceType) {
        var services = new ServiceCollection();

        new BenchmarkTestApp().PopulateServiceCollection(services);

        return services
            .Where(descriptor => descriptor.ServiceType == serviceType)
            .Select(descriptor => descriptor.ImplementationType?.Name ?? "factory")
            .ToList();
    }

    /// <summary>
    /// The set has more than one member, which is the property <c>Try</c> silently destroys.
    /// </summary>
    [Fact]
    public void ResponseSerializersResolveAsASetRatherThanASingleWinner() {
        var serializers = RegistrationsFor(typeof(IResponseSerializer));

        Assert.Contains("SystemTextJsonResponseSerializer", serializers);
        Assert.True(serializers.Count > 1);
    }
}
