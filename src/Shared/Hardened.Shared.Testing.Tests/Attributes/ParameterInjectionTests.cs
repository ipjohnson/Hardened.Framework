using DependencyModules.Testing.Impl;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Impl;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using HardenedTestContext = Hardened.Shared.Testing.Impl.TestContext;

namespace Hardened.Shared.Testing.Tests.Attributes;

/// <summary>
/// What a <c>[HardenedTest]</c> method's parameters are filled with, and where each value comes
/// from.
/// </summary>
/// <remarks>
/// <para>
/// These drive the resolution pipeline by hand rather than by declaring <c>[HardenedTest]</c>
/// methods and inspecting their own arguments. A test that receives its arguments from the
/// machinery it is testing cannot fail usefully: if injection silently supplied the wrong thing, the
/// assertion inside it is reading the wrong thing too; and the one case that matters most — a
/// parameter nothing can supply — aborts the test before any assertion runs, so it can only be
/// observed from outside.
/// </para>
/// <para>
/// <see cref="BuildArguments{T}"/> reproduces the order <c>ModuleTestCase</c> uses, which is the
/// part that decides who wins: the application's modules register first, then parameter providers,
/// then the entry point.
/// </para>
/// </remarks>
public class ParameterInjectionTests {

    private class InjectionTargets {
        public void AService(IGreetingService greetingService) { }

        public void TheTestContext(ITestContext context) { }

        public void AMockedService([Mock] IGreetingService greetingService) { }

        public void ServiceContextAndMockTogether(
            ITestContext context,
            GreetingConsumer consumer,
            [Mock] IGreetingService greetingService) { }

        public void SomethingNothingCanSupply(INeverRegisteredService service) { }

        public void TheContainerItself(IServiceProvider provider) { }
    }

    private static (TestParameterResolver Resolver, ServiceProvider Provider) BuildContainer<T>(string methodName) {
        var context = FakeTestMethodContext.For<T>(methodName);
        var collection = new ServiceCollection();

        // 1. the application's own registrations, as the entry point's module supplies them
        new AssemblyEntryPointModule().ConfigureServices(collection);

        // 2. parameter providers — this is where [Mock] puts its substitute, after the application
        var resolver = new TestParameterResolver(context);
        resolver.SetupServiceCollection(collection);

        // 3. the harness's own registrations
        new HardenedTestEntryPointAttribute(typeof(AssemblyEntryPointModule))
            .SetupServiceCollection(context, collection);

        return (resolver, collection.BuildServiceProvider());
    }

    private static async Task<object?[]> BuildArguments<T>(string methodName) {
        var (resolver, provider) = BuildContainer<T>(methodName);

        return await resolver.ResolveArgumentsAsync(provider, []);
    }

    [Fact]
    public async Task AServiceParameterComesFromTheApplicationsOwnRegistration() {
        var arguments = await BuildArguments<InjectionTargets>(nameof(InjectionTargets.AService));

        Assert.IsType<RealGreetingService>(Assert.Single(arguments));
    }

    [Fact]
    public async Task TheTestContextIsInjectableLikeAnyOtherService() {
        var arguments = await BuildArguments<InjectionTargets>(nameof(InjectionTargets.TheTestContext));

        var context = Assert.IsType<HardenedTestContext>(Assert.Single(arguments));

        Assert.NotNull(context.Retry);
    }

    [Fact]
    public async Task AMockParameterIsASubstituteAndNotTheRealService() {
        var arguments = await BuildArguments<InjectionTargets>(nameof(InjectionTargets.AMockedService));

        var greeting = Assert.IsAssignableFrom<IGreetingService>(Assert.Single(arguments));

        Assert.IsNotType<RealGreetingService>(greeting);

        greeting.Greet("world").Returns("substituted");
        Assert.Equal("substituted", greeting.Greet("world"));
    }

    /// <summary>
    /// The behaviour that makes <c>[Mock]</c> worth more than constructing a substitute in the test
    /// body: it is registered after the application's own service, so everything the container
    /// builds afterwards is built against the substitute. A <c>[Mock]</c> the test alone could see
    /// would leave the service under test talking to the real implementation.
    /// </summary>
    [Fact]
    public async Task AMockReplacesTheApplicationsServiceForEverythingElseInTheContainer() {
        var arguments = await BuildArguments<InjectionTargets>(
            nameof(InjectionTargets.ServiceContextAndMockTogether));

        var consumer = Assert.IsType<GreetingConsumer>(arguments[1]);
        var greeting = Assert.IsAssignableFrom<IGreetingService>(arguments[2]);

        Assert.Same(greeting, consumer.GreetingService);
        Assert.IsNotType<RealGreetingService>(consumer.GreetingService);
    }

    [Fact]
    public async Task AMockedDependencyDrivesTheServiceUnderTest() {
        var arguments = await BuildArguments<InjectionTargets>(
            nameof(InjectionTargets.ServiceContextAndMockTogether));

        var consumer = Assert.IsType<GreetingConsumer>(arguments[1]);
        var greeting = Assert.IsAssignableFrom<IGreetingService>(arguments[2]);

        greeting.Greet("world").Returns("substituted");

        Assert.Equal("substituted", consumer.GreetWorld());
        greeting.Received(1).Greet("world");
    }

    [Fact]
    public async Task ContextServiceAndMockParametersAreEachSuppliedInOneCall() {
        var arguments = await BuildArguments<InjectionTargets>(
            nameof(InjectionTargets.ServiceContextAndMockTogether));

        Assert.Equal(3, arguments.Length);
        Assert.IsType<HardenedTestContext>(arguments[0]);
        Assert.IsType<GreetingConsumer>(arguments[1]);
        Assert.IsAssignableFrom<IGreetingService>(arguments[2]);
    }

    /// <summary>
    /// An interface nothing registers cannot be constructed either, so resolution fails rather than
    /// handing the test a null it would dereference several lines later.
    /// </summary>
    /// <remarks>
    /// The type of the failure is asserted and its message is not. As of 2026-08-11 the message is
    /// "Instances of abstract classes cannot be created.", which names neither the parameter nor its
    /// type — reported as a diagnostic gap rather than pinned here, so that improving it does not
    /// break this test.
    /// </remarks>
    [Fact]
    public async Task AParameterNothingCanSupplyFailsRatherThanArrivingAsNull() {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildArguments<InjectionTargets>(nameof(InjectionTargets.SomethingNothingCanSupply)));
    }

    [Fact]
    public async Task TheContainerItselfCanBeAskedForByParameter() {
        var (resolver, provider) = BuildContainer<InjectionTargets>(nameof(InjectionTargets.TheContainerItself));

        var arguments = await resolver.ResolveArgumentsAsync(provider, []);

        Assert.Same(provider, Assert.Single(arguments));
    }

    /// <summary>
    /// Registration has to happen while the collection is still open. Resolving without it would
    /// skip every parameter attribute, and a <c>[Mock]</c> parameter would quietly hand back the
    /// real service — so the resolver refuses instead.
    /// </summary>
    [Fact]
    public async Task ResolvingWithoutTheRegistrationPassIsRefused() {
        var resolver = new TestParameterResolver(
            FakeTestMethodContext.For<InjectionTargets>(nameof(InjectionTargets.AMockedService)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveArgumentsAsync(new ServiceCollection().BuildServiceProvider(), []));
    }
}
