using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// That the framework's JSON reader survives a second one being registered.
/// </summary>
/// <remarks>
/// <para>
/// <c>HardenedRequestModule</c> registered it as
/// <c>TryAddSingleton&lt;IRequestDeserializer, SystemTextJsonRequestDeserializer&gt;</c>, which keys
/// the Try on the service type rather than on the class. Any package registering an
/// <c>IRequestDeserializer</c> ahead of it - which is what importing a MessagePack or protobuf
/// serializer does - skipped the line, and the application was left with no JSON reader at all.
/// Every request carrying a JSON body then answered 500 with "Could not find serializer".
/// </para>
/// <para>
/// The response side carried the identical defect and was fixed the identical way. Precedence among
/// readers is <c>RequestDeserializerOrder</c>'s, which the locator sorts on, so the Try was never
/// carrying it.
/// </para>
/// </remarks>
public class RequestDeserializerRegistrationTests {

    /// <summary>A reader for one content type, the shape a serializer package registers.</summary>
    private sealed class SpecialisedDeserializer : IRequestDeserializer {
        public bool IsDefaultSerializer => false;

        public int Order => (int)RequestDeserializerOrder.Specialized;

        public bool CanProcessContext(IExecutionContext context) => false;

        public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) =>
            new(default(T));
    }

    private static IServiceCollection Configured(bool withSpecialised) {
        var services = new ServiceCollection();

        // Before the framework module, which is the order DependencyModules applies: dependencies
        // first, then the module that imported them.
        if (withSpecialised) {
            services.AddTransient<IRequestDeserializer, SpecialisedDeserializer>();
        }

        new HardenedRequestModule().ConfigureServices(services);

        return services;
    }

    /// <summary>
    /// Named as either half of a descriptor, so this reads the same against the registration that
    /// carried the defect - which named the class only as an implementation type.
    /// </summary>
    private static bool RegistersTheJsonReader(IServiceCollection services) =>
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(Runtime.Serializer.SystemTextJsonRequestDeserializer) ||
            descriptor.ImplementationType == typeof(Runtime.Serializer.SystemTextJsonRequestDeserializer));

    [Fact]
    public void TheJsonReaderIsRegistered() =>
        Assert.True(RegistersTheJsonReader(Configured(withSpecialised: false)));

    [Fact]
    public void AndStillIsWhenAPackageRegisteredOneFirst() =>
        Assert.True(RegistersTheJsonReader(Configured(withSpecialised: true)));

    /// <summary>
    /// Both readers reach the locator, rather than the second replacing the first.
    /// </summary>
    [Fact]
    public void BothReadersAreRegistered() {
        var deserializers = Configured(withSpecialised: true)
            .Where(descriptor => descriptor.ServiceType == typeof(IRequestDeserializer))
            .ToList();

        Assert.Equal(2, deserializers.Count);
    }
}
