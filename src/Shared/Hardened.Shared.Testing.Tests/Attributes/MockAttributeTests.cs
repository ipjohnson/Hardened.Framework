using System.Reflection;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Shared.Testing.Tests.Attributes;

/// <summary>
/// <see cref="MockAttribute"/> on its own, without a resolver in the way.
/// </summary>
public class MockAttributeTests {

    private static ParameterInfo GreetingParameter() =>
        typeof(MockAttributeTests)
            .GetMethod(nameof(TakesAGreetingService), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

    private static void TakesAGreetingService([Mock] IGreetingService greetingService) { }

    private static FakeTestMethodContext Context() =>
        FakeTestMethodContext.For<MockAttributeTests>(nameof(TakesAGreetingService));

    [Fact]
    public void SetupRegistersASubstituteForTheParametersType() {
        var collection = new ServiceCollection();

        new MockAttribute().SetupServiceCollection(Context(), collection, GreetingParameter());

        var greeting = collection.BuildServiceProvider().GetRequiredService<IGreetingService>();

        greeting.Greet("x").Returns("substituted");
        Assert.Equal("substituted", greeting.Greet("x"));
    }

    /// <summary>
    /// The substitute is added, not swapped in, and Microsoft's container resolves the last
    /// registration for a service — so ordering is the whole mechanism by which a mock beats the
    /// application. Registering it first would leave the application's service winning.
    /// </summary>
    [Fact]
    public void TheSubstituteIsAppendedAfterTheApplicationsRegistrationAndThereforeWins() {
        var collection = new ServiceCollection();
        collection.AddSingleton<IGreetingService, RealGreetingService>();

        new MockAttribute().SetupServiceCollection(Context(), collection, GreetingParameter());

        Assert.Equal(typeof(RealGreetingService), collection[0].ImplementationType);
        Assert.IsNotType<RealGreetingService>(
            collection.BuildServiceProvider().GetRequiredService<IGreetingService>());
    }

    [Fact]
    public void TheApplicationsRegistrationIsLeftInPlaceRatherThanRemoved() {
        var collection = new ServiceCollection();
        collection.AddSingleton<IGreetingService, RealGreetingService>();

        new MockAttribute().SetupServiceCollection(Context(), collection, GreetingParameter());

        Assert.Equal(2, collection.Count(descriptor => descriptor.ServiceType == typeof(IGreetingService)));
    }

    [Fact]
    public void EveryConsumerGetsTheOneSubstituteRatherThanOnePerInjection() {
        var collection = new ServiceCollection();

        new MockAttribute().SetupServiceCollection(Context(), collection, GreetingParameter());

        var provider = collection.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IGreetingService>(),
            provider.GetRequiredService<IGreetingService>());
    }

    [Fact]
    public async Task TheValueSuppliedForTheParameterIsTheRegisteredSubstitute() {
        var collection = new ServiceCollection();
        var attribute = new MockAttribute();

        attribute.SetupServiceCollection(Context(), collection, GreetingParameter());

        var provider = collection.BuildServiceProvider();
        var value = await attribute.GetParameterValueAsync(Context(), provider, GreetingParameter());

        Assert.Same(provider.GetRequiredService<IGreetingService>(), value);
    }

    /// <summary>
    /// Returning null is how a value provider stands aside, letting the next one — or the container
    /// itself — answer for the parameter.
    /// </summary>
    [Fact]
    public async Task AskingForAValueWithNothingRegisteredStandsAside() {
        var provider = new ServiceCollection().BuildServiceProvider();

        Assert.Null(await new MockAttribute()
            .GetParameterValueAsync(Context(), provider, GreetingParameter()));
    }
}
