using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// What <c>[AotSerializerModule]</c> puts in the container.
/// </summary>
/// <remarks>
/// <para>
/// The module was at 0% — nothing in the suite applied the attribute or called the module, so an
/// application published with Native AOT ran a code path no test had executed. The AOT integration
/// fixture exercises it end to end, and CI publishes that fixture, but a publish check cannot say
/// <em>which</em> serializer answered.
/// </para>
/// <para>
/// <see cref="TheTwoRegistrationMethodsRegisterTheSameThings"/> exists because the class carries
/// two methods with byte-identical bodies — <c>PopulateServiceCollection</c> from
/// <c>IDependencyModule</c> and <c>InternalApplyServices</c>, which the module infrastructure calls.
/// Duplicated bodies drift; this fails when one is edited and the other is not.
/// </para>
/// </remarks>
public class AotSerializerModuleTests {

    private static ServiceCollection Applied() {
        var services = new ServiceCollection();

        new AotSerializerModule().PopulateServiceCollection(services);

        return services;
    }

    private static ServiceDescriptor DescriptorFor<TService>(IServiceCollection services) =>
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService));

    [Fact]
    public void TheResponseSerializerIsTheAotOne() {
        Assert.Equal(
            typeof(AotResponseSerializer), DescriptorFor<IResponseSerializer>(Applied()).ImplementationType);
    }

    [Fact]
    public void TheRequestDeserializerIsTheAotOne() {
        Assert.Equal(
            typeof(AotRequestDeserializer), DescriptorFor<IRequestDeserializer>(Applied()).ImplementationType);
    }

    [Fact]
    public void TheJsonSerializerIsTheAotOne() {
        Assert.Equal(
            typeof(AotJsonSerializer), DescriptorFor<IJsonSerializer>(Applied()).ImplementationType);
    }

    /// <summary>
    /// Singletons, not transients. The AOT serializers build their resolver chain in the
    /// constructor, so a transient rebuilds it per resolution.
    /// </summary>
    [Theory]
    [InlineData(typeof(IResponseSerializer))]
    [InlineData(typeof(IRequestDeserializer))]
    [InlineData(typeof(IJsonSerializer))]
    public void EveryRegistrationIsASingleton(Type serviceType) {
        var descriptor = Assert.Single(Applied(), item => item.ServiceType == serviceType);

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void NothingElseIsRegistered() {
        Assert.Equal(3, Applied().Count);
    }

    /// <summary>
    /// Two identical bodies on one class. This is what notices when only one of them is edited.
    /// </summary>
    [Fact]
    public void TheTwoRegistrationMethodsRegisterTheSameThings() {
        var viaInterface = new ServiceCollection();
        var viaInternal = new ServiceCollection();

        new AotSerializerModule().PopulateServiceCollection(viaInterface);
        new AotSerializerModule().InternalApplyServices(viaInternal);

        Assert.Equal(
            viaInterface.Select(descriptor => (descriptor.ServiceType, descriptor.ImplementationType, descriptor.Lifetime)),
            viaInternal.Select(descriptor => (descriptor.ServiceType, descriptor.ImplementationType, descriptor.Lifetime)));
    }

    #region module identity

    /// <summary>
    /// Modules are de-duplicated by equality, so an application reaching this one down two
    /// different import paths must apply it once. Registering twice would put two
    /// <c>IResponseSerializer</c>s of the same type in the container.
    /// </summary>
    [Fact]
    public void AnyTwoInstancesAreEqual() {
        Assert.Equal(new AotSerializerModule(), new AotSerializerModule());
    }

    [Fact]
    public void EqualInstancesShareAHashCode() {
        Assert.Equal(new AotSerializerModule().GetHashCode(), new AotSerializerModule().GetHashCode());
    }

    [Fact]
    public void AnotherTypeIsNotEqual() {
        Assert.False(new AotSerializerModule().Equals(new object()));
    }

    [Fact]
    public void NullIsNotEqual() {
        Assert.False(new AotSerializerModule().Equals(null));
    }

    #endregion

    [Fact]
    public void TheAttributeProvidesTheModule() {
        Assert.IsType<AotSerializerModule>(new AotSerializerModuleAttribute().GetModule());
    }

    /// <summary>
    /// The attribute hands back a module equal to any other, so applying it in two places in an
    /// application still de-duplicates.
    /// </summary>
    [Fact]
    public void ModulesFromTwoAttributesAreEqual() {
        Assert.Equal(
            new AotSerializerModuleAttribute().GetModule(),
            new AotSerializerModuleAttribute().GetModule());
    }
}
