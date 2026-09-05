using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Impl;
using Hardened.Shared.Testing.Logging;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using HardenedTestContext = Hardened.Shared.Testing.Impl.TestContext;

namespace Hardened.Shared.Testing.Tests.Attributes;

/// <summary>
/// <see cref="HardenedTestEntryPointAttribute"/> is what turns a test method into an application:
/// it builds the environment, runs every registration hook in scope, and hands the container back
/// for the test's parameters to be resolved from.
/// </summary>
/// <remarks>
/// Driven directly rather than through <c>[HardenedTest]</c>. The attribute's whole job is to decide
/// what a test can see, so a test that could only observe it from inside the container it built
/// would be reporting on itself — a hook that silently never ran would leave the assertion looking
/// at exactly the same container as one that did.
/// </remarks>
public class HardenedTestEntryPointSetupTests {

    private static (IServiceProvider Provider, ServiceCollection Collection) Setup<T>(
        string methodName, Action<ServiceCollection>? beforeSetup = null) {
        var collection = new ServiceCollection();
        collection.AddSingleton(new StartupLog());
        beforeSetup?.Invoke(collection);

        // Driven directly, so nothing has loaded the runner package for this test; the attribute
        // registers the logger provider of whatever runner is installed.
        XunitCurrentTestProvider.Install();

        new HardenedTestEntryPointAttribute(typeof(AssemblyEntryPointModule))
            .SetupServiceCollection(FakeTestMethodContext.For<T>(methodName), collection);

        return (collection.BuildServiceProvider(), collection);
    }

    private class UsesTheAssemblyEnvironment {
        public void Method() { }
    }

    [EnvironmentName("class-environment")]
    [EnvironmentValue("class-scoped-value", "from-class")]
    private class DeclaresClassLevelEnvironment {
        public void Method() { }

        [EnvironmentName("method-environment")]
        [EnvironmentValue("method-scoped-value", "from-method")]
        public void MethodWithItsOwnEnvironment() { }
    }

    // ---- environment name, across the three scopes -------------------------------------------

    /// <summary>
    /// The documented fallback, and the only one of the four rungs that cannot be reached from this
    /// assembly — Bootstrap.cs declares an assembly-level name, which is what the three tests below
    /// measure against. A method in an assembly that declares nothing gets "test".
    /// </summary>
    [Fact]
    public void AnAssemblyThatNamesNoEnvironmentGetsTest() {
        var (provider, _) = Setup<ApplicationLogic>(nameof(ApplicationLogic.Start));

        Assert.Equal("test", provider.GetRequiredService<IHardenedEnvironment>().Name);
    }

    [Fact]
    public void TheAssemblyEnvironmentNameAppliesWhenNothingNarrowerNamesOne() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method));

        Assert.Equal("assembly-environment", provider.GetRequiredService<IHardenedEnvironment>().Name);
    }

    [Fact]
    public void AClassEnvironmentNameBeatsTheAssemblys() {
        var (provider, _) = Setup<DeclaresClassLevelEnvironment>(nameof(DeclaresClassLevelEnvironment.Method));

        Assert.Equal("class-environment", provider.GetRequiredService<IHardenedEnvironment>().Name);
    }

    [Fact]
    public void AMethodEnvironmentNameBeatsBothTheClassAndTheAssembly() {
        var (provider, _) = Setup<DeclaresClassLevelEnvironment>(
            nameof(DeclaresClassLevelEnvironment.MethodWithItsOwnEnvironment));

        Assert.Equal("method-environment", provider.GetRequiredService<IHardenedEnvironment>().Name);
    }

    // ---- environment values ------------------------------------------------------------------

    /// <summary>
    /// Values accumulate rather than override: a method keeps everything its class and its assembly
    /// declared, and adds its own.
    /// </summary>
    [Fact]
    public void EnvironmentValuesFromEveryScopeAreMerged() {
        var (provider, _) = Setup<DeclaresClassLevelEnvironment>(
            nameof(DeclaresClassLevelEnvironment.MethodWithItsOwnEnvironment));

        var environment = provider.GetRequiredService<IHardenedEnvironment>();

        Assert.Equal("from-method", environment.Value<string>("method-scoped-value"));
        Assert.Equal("from-class", environment.Value<string>("class-scoped-value"));
        Assert.Equal("from-assembly", environment.Value<string>("assembly-scoped-value"));
    }

    [Fact]
    public void AMethodDoesNotSeeValuesDeclaredOnAnUnrelatedClass() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method));

        var environment = provider.GetRequiredService<IHardenedEnvironment>();

        Assert.Null(environment.Value<string>("class-scoped-value"));
        Assert.Equal("from-assembly", environment.Value<string>("assembly-scoped-value"));
    }

    [RecordingEnvironment("hook-written-value", "written-by-hook")]
    private class DeclaresAnEnvironmentHook {
        public void Method() { }
    }

    [Fact]
    public void EnvironmentHooksCanAddValuesOfTheirOwn() {
        var (provider, _) = Setup<DeclaresAnEnvironmentHook>(nameof(DeclaresAnEnvironmentHook.Method));

        Assert.Equal("written-by-hook",
            provider.GetRequiredService<IHardenedEnvironment>().Value<string>("hook-written-value"));
    }

    /// <summary>
    /// The name is resolved before the hooks run, so a hook can branch on it rather than having to
    /// read the attributes again itself.
    /// </summary>
    [Fact]
    public void EnvironmentHooksAreToldTheResolvedEnvironmentName() {
        var (provider, _) = Setup<DeclaresAnEnvironmentHook>(nameof(DeclaresAnEnvironmentHook.Method));

        Assert.Equal("assembly-environment",
            provider.GetRequiredService<IHardenedEnvironment>()
                .Value<string>("environment-name-seen-by-configure"));
    }

    // ---- ordered hooks -----------------------------------------------------------------------

    [RecordingRegistration("class-first", Order = 1)]
    [RecordingParameterProvider("class-provider", Order = 1)]
    private class DeclaresOrderedHooks {
        [RecordingRegistration("method-last", Order = 99)]
        [RecordingRegistration("method-middle", Order = 50)]
        [RecordingParameterProvider("method-provider", Order = 99)]
        public void Method() { }
    }

    /// <summary>
    /// Order outranks where the attribute was declared. A hook on the class runs before one on the
    /// method when it asks for a lower order, which is what lets a package guarantee it registers
    /// before the tests that consume it.
    /// </summary>
    [Fact]
    public void RegistrationHooksRunInDeclaredOrderNotScopeOrder() {
        var (_, collection) = Setup<DeclaresOrderedHooks>(nameof(DeclaresOrderedHooks.Method));

        Assert.Equal(
            new[] { "class-first", "method-middle", "method-last" },
            collection.Select(descriptor => descriptor.ImplementationInstance)
                .OfType<RegistrationMark>()
                .Select(mark => mark.Name));
    }

    [Fact]
    public void ParameterProviderHooksRunInDeclaredOrderNotScopeOrder() {
        var (_, collection) = Setup<DeclaresOrderedHooks>(nameof(DeclaresOrderedHooks.Method));

        Assert.Equal(
            new[] { "class-provider", "method-provider" },
            collection.Select(descriptor => descriptor.ImplementationInstance)
                .OfType<ParameterProviderMark>()
                .Select(mark => mark.Name));
    }

    /// <summary>
    /// Parameter providers get a registration pass with no parameter in hand, so they can put
    /// services in place once for the whole test rather than once per parameter that names them.
    /// </summary>
    [Fact]
    public void ParameterProviderHooksRegisterWithNoParameterInHand() {
        var (_, collection) = Setup<DeclaresOrderedHooks>(nameof(DeclaresOrderedHooks.Method));

        var marks = collection.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ParameterProviderMark>();

        Assert.All(marks, mark => Assert.DoesNotContain(":", mark.Name));
    }

    [RecordingConfiguration("class-config", Order = 1)]
    private class DeclaresConfigurationHooks {
        [RecordingConfiguration("method-config", Order = 5)]
        public void Method() { }
    }

    [Fact]
    public void ConfigurationHooksAmendTheRegisteredConfigurationPackageInDeclaredOrder() {
        var (provider, _) = Setup<DeclaresConfigurationHooks>(nameof(DeclaresConfigurationHooks.Method));

        var environment = provider.GetRequiredService<IHardenedEnvironment>();
        var package = provider.GetRequiredService<IConfigurationPackage>();

        var log = new ConfigurationLog();
        foreach (var amender in package.ConfigurationValueAmenders(environment)) {
            amender.ApplyConfiguration(environment, log);
        }

        Assert.Equal(new[] { "class-config", "method-config" }, log.Names);
    }

    // ---- startup ------------------------------------------------------------------------------

    [RecordingStartup("class-startup", Order = 1)]
    private class DeclaresStartupHooks {
        [RecordingStartup("method-startup-last", Order = 99)]
        [RecordingStartup("method-startup-middle", Order = 50)]
        public void Method() { }
    }

    [Fact]
    public async Task StartupHooksRunInDeclaredOrderNotScopeOrder() {
        var (provider, _) = Setup<DeclaresStartupHooks>(nameof(DeclaresStartupHooks.Method));

        await new HardenedTestEntryPointAttribute(typeof(AssemblyEntryPointModule))
            .StartupAsync(FakeTestMethodContext.For<DeclaresStartupHooks>(nameof(DeclaresStartupHooks.Method)),
                provider);

        Assert.Equal(
            new[] { "class-startup", "method-startup-middle", "method-startup-last" },
            provider.GetRequiredService<StartupLog>().Names);
    }

    private sealed class RecordingStartupService : IStartupService {
        public bool Ran { get; private set; }

        public Task<bool> Startup(IServiceProvider rootProvider) {
            Ran = true;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// The application's own startup services run before the test's, so a test never sees a
    /// half-started application.
    /// </summary>
    [Fact]
    public async Task ApplicationStartupServicesRunBeforeTheTestMethod() {
        var startupService = new RecordingStartupService();

        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(
            nameof(UsesTheAssemblyEnvironment.Method),
            collection => collection.AddSingleton<IStartupService>(startupService));

        await new HardenedTestEntryPointAttribute(typeof(AssemblyEntryPointModule))
            .StartupAsync(
                FakeTestMethodContext.For<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method)),
                provider);

        Assert.True(startupService.Ran);
    }

    // ---- what the container always carries ----------------------------------------------------

    /// <summary>
    /// A service handed the application root resolves the same singletons the test does, so the two
    /// are looking at one application rather than at two copies of its registrations.
    /// </summary>
    /// <remarks>
    /// Identity of the provider object itself is deliberately not asserted. The root is built from
    /// the <c>IServiceProvider</c> passed to its factory, which is the container's root scope rather
    /// than the <c>ServiceProvider</c> the caller holds — a different object reaching the same
    /// registrations.
    /// </remarks>
    [Fact]
    public void TheApplicationRootResolvesFromTheSameContainerAsTheTest() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method));

        var root = provider.GetRequiredService<IApplicationRoot>();

        Assert.Same(
            provider.GetRequiredService<ITestContext>(),
            root.Provider.GetRequiredService<ITestContext>());
    }

    /// <summary>
    /// Disposing the root does not tear the container down: the harness built it and the harness
    /// disposes it, so a test that disposes what it was given does not take the container with it.
    /// </summary>
    [Fact]
    public async Task DisposingTheApplicationRootLeavesTheContainerUsable() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method));

        var root = provider.GetRequiredService<IApplicationRoot>();

        await root.DisposeAsync();

        Assert.NotNull(provider.GetRequiredService<ITestContext>());
    }

    [Fact]
    public void ATestContextIsRegisteredWithARetryEngineAttached() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method));

        var context = provider.GetRequiredService<ITestContext>();

        Assert.IsType<HardenedTestContext>(context);
        Assert.IsType<RetryEngine>(context.Retry);
    }

    /// <summary>
    /// The context takes its token from the registered <see cref="TestCancellationToken"/>, which is
    /// the single place a run's cancellation would come from.
    /// </summary>
    [Fact]
    public void TheTestContextTakesItsCancellationTokenFromTheRegisteredOne() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method));

        Assert.Equal(
            provider.GetRequiredService<TestCancellationToken>().Token,
            provider.GetRequiredService<ITestContext>().CancellationRequest);
    }

    [Fact]
    public void TheTestContextIsSharedAcrossEveryResolutionInOneTest() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method));

        Assert.Same(provider.GetRequiredService<ITestContext>(), provider.GetRequiredService<ITestContext>());
    }

    private sealed class UnwantedLoggerProvider : ILoggerProvider {
        public void Dispose() { }

        public ILogger CreateLogger(string categoryName) => throw new NotSupportedException();
    }

    /// <summary>
    /// Logging is redirected to xUnit's output, and any provider the application registered is
    /// removed rather than added to — otherwise a test run writes to the application's real sinks.
    /// </summary>
    [Fact]
    public void TheApplicationsOwnLoggerProvidersAreReplacedNotJoined() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(
            nameof(UsesTheAssemblyEnvironment.Method),
            collection => collection.AddSingleton<ILoggerProvider, UnwantedLoggerProvider>());

        Assert.IsType<XunitLoggerProvider>(Assert.Single(provider.GetServices<ILoggerProvider>()));
    }

    [Fact]
    public void TheEnvironmentIsResolvableByServicesUnderTest() {
        var (provider, _) = Setup<UsesTheAssemblyEnvironment>(nameof(UsesTheAssemblyEnvironment.Method));

        Assert.IsType<TestEnvironment>(provider.GetRequiredService<IHardenedEnvironment>());
    }

    /// <summary>
    /// The same environment answers under both service types, so the module system and application
    /// code agree about which environment a test is running in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The harness used to register the environment under <see cref="IHardenedEnvironment"/> alone.
    /// <see cref="IModuleEnvironment"/> then fell back to its own default and reported
    /// <c>Production</c> however the test was annotated, which made <c>[IfEnvironment]</c> — a
    /// shipped, template-default feature — impossible to exercise from a test at all.
    /// </para>
    /// <para>
    /// Asserted as identity rather than as two equal names, because two objects agreeing today is
    /// what the previous version looked like from the outside right up until one of them was asked
    /// something the other would have answered differently.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEnvironmentIsRegisteredUnderBothInterfaces() {
        var (provider, _) = Setup<DeclaresClassLevelEnvironment>(
            nameof(DeclaresClassLevelEnvironment.Method));

        var hardened = provider.GetRequiredService<IHardenedEnvironment>();
        var module = provider.GetRequiredService<IModuleEnvironment>();

        Assert.Same(hardened, module);
        Assert.Equal("class-environment", hardened.Name);
        Assert.Equal("class-environment", module.EnvironmentName);
    }
}
