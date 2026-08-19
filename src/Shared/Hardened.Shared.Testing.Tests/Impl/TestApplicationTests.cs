using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Impl;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Shared.Testing.Tests.Impl;

/// <summary>
/// The application root every Hardened test host is built on.
/// </summary>
/// <remarks>
/// <para>
/// It was at <b>0% line coverage</b> — nineteen lines, in a package that ships, underneath every
/// consumer's test suite. It ran constantly and nothing asserted anything about it.
/// </para>
/// <para>
/// Two things here are worth pinning. <b>Overrides run after the module</b>, which is what makes
/// <c>[Mock]</c> able to beat an application's own registration — reverse that and every mock in
/// every consumer's suite silently stops taking effect while the tests still pass, because the real
/// service usually works. And <b>the constructor runs startup services</b>, so a test host reaches
/// a handler with the same wiring production would have.
/// </para>
/// <para>
/// The two constructors have near-identical bodies, one per module shape.
/// <see cref="BothModuleShapesProduceTheSameWiring"/> is what notices when only one is edited.
/// </para>
/// </remarks>
public class TestApplicationTests {

    private static IHardenedEnvironment Environment() => new EnvironmentImpl();

    /// <summary>An <see cref="IApplicationModule"/> that registers whatever it is handed.</summary>
    private sealed class ApplicationModule : IApplicationModule {
        private readonly Action<IHardenedEnvironment, IServiceCollection> _configure;

        public ApplicationModule(Action<IHardenedEnvironment, IServiceCollection> configure) {
            _configure = configure;
        }

        public void ConfigureModule(IHardenedEnvironment environment, IServiceCollection services) =>
            _configure(environment, services);
    }

    /// <summary>An <see cref="IDependencyModule"/> that registers whatever it is handed.</summary>
    private sealed class DependencyModule : IDependencyModule {
        private readonly Action<IServiceCollection> _configure;

        public DependencyModule(Action<IServiceCollection> configure) {
            _configure = configure;
        }

        public void PopulateServiceCollection(IServiceCollection services) => _configure(services);
    }

    private static TestApplication FromApplicationModule(
        Action<IHardenedEnvironment, IServiceCollection>? configure = null,
        Action<IHardenedEnvironment, IServiceCollection>? overrides = null) =>
        new(new ApplicationModule(configure ?? ((_, _) => { })), "test", Environment(), overrides);

    private static TestApplication FromDependencyModule(
        Action<IServiceCollection>? configure = null,
        Action<IHardenedEnvironment, IServiceCollection>? overrides = null) =>
        new(new DependencyModule(configure ?? (_ => { })), "test", Environment(), overrides);

    #region what the container ends up with

    [Fact]
    public async Task TheModulesRegistrationsReachTheProvider() {
        await using var application = FromApplicationModule(
            (_, services) => services.AddSingleton<IGreetingService, RealGreetingService>());

        Assert.Equal(
            "real hello world",
            application.Provider.GetRequiredService<IGreetingService>().Greet("world"));
    }

    [Fact]
    public async Task ADependencyModulesRegistrationsReachTheProviderToo() {
        await using var application = FromDependencyModule(
            services => services.AddSingleton<IGreetingService, RealGreetingService>());

        Assert.Equal(
            "real hello world",
            application.Provider.GetRequiredService<IGreetingService>().Greet("world"));
    }

    /// <summary>
    /// Logging is registered before the module, so a module resolving a logger does not have to
    /// bring its own stack.
    /// </summary>
    [Fact]
    public async Task LoggingIsAvailableWithoutTheModuleRegisteringIt() {
        await using var application = FromApplicationModule();

        Assert.NotNull(application.Provider.GetRequiredService<ILogger<TestApplicationTests>>());
    }

    [Fact]
    public async Task TheEnvironmentIsResolvable() {
        var environment = Environment();

        await using var application = new TestApplication(
            new ApplicationModule((_, _) => { }), "test", environment, null);

        Assert.Same(environment, application.Provider.GetRequiredService<IHardenedEnvironment>());
    }

    /// <summary>
    /// The module is handed the same environment the container gets, so a module that branches on
    /// it and a service that reads it agree.
    /// </summary>
    [Fact]
    public async Task TheModuleIsGivenTheSameEnvironment() {
        var environment = Environment();
        IHardenedEnvironment? seen = null;

        await using var application = new TestApplication(
            new ApplicationModule((given, _) => seen = given), "test", environment, null);

        Assert.Same(environment, seen);
    }

    #endregion

    #region overrides

    /// <summary>
    /// <b>Overrides run after the module.</b> This is what lets <c>[Mock]</c> beat an application's
    /// own registration. Reversed, every mock in every consumer's suite would silently stop taking
    /// effect — and the tests would mostly still pass, because the real service usually works.
    /// </summary>
    [Fact]
    public async Task AnOverrideReplacesTheModulesRegistration() {
        await using var application = FromApplicationModule(
            (_, services) => services.AddSingleton<IGreetingService, RealGreetingService>(),
            (_, services) => services.AddSingleton<IGreetingService>(new StubGreeting()));

        Assert.Equal(
            "stub hello world",
            application.Provider.GetRequiredService<IGreetingService>().Greet("world"));
    }

    [Fact]
    public async Task AnOverrideReplacesADependencyModulesRegistrationToo() {
        await using var application = FromDependencyModule(
            services => services.AddSingleton<IGreetingService, RealGreetingService>(),
            (_, services) => services.AddSingleton<IGreetingService>(new StubGreeting()));

        Assert.Equal(
            "stub hello world",
            application.Provider.GetRequiredService<IGreetingService>().Greet("world"));
    }

    [Fact]
    public async Task AnOverrideMayAddAServiceTheModuleNeverRegistered() {
        await using var application = FromApplicationModule(
            overrides: (_, services) => services.AddSingleton<IGreetingService>(new StubGreeting()));

        Assert.Equal(
            "stub hello world",
            application.Provider.GetRequiredService<IGreetingService>().Greet("world"));
    }

    [Fact]
    public async Task NoOverrideIsFine() {
        await using var application = FromApplicationModule();

        Assert.NotNull(application.Provider);
    }

    [Fact]
    public async Task TheOverrideIsGivenTheEnvironment() {
        var environment = Environment();
        IHardenedEnvironment? seen = null;

        await using var application = new TestApplication(
            new ApplicationModule((_, _) => { }), "test", environment,
            (given, _) => seen = given);

        Assert.Same(environment, seen);
    }

    #endregion

    #region startup

    /// <summary>
    /// The constructor runs startup services, so a test host reaches a handler with the wiring
    /// production would have rather than one that skipped every startup step.
    /// </summary>
    [Fact]
    public async Task StartupServicesRunBeforeTheConstructorReturns() {
        var startup = new RecordingStartupService();

        await using var application = FromApplicationModule(
            (_, services) => services.AddSingleton<IStartupService>(startup));

        Assert.True(startup.Ran, "the startup service had not run when the constructor returned");
    }

    [Fact]
    public async Task EveryStartupServiceRuns() {
        var first = new RecordingStartupService();
        var second = new RecordingStartupService();

        await using var application = FromApplicationModule((_, services) => {
            services.AddSingleton<IStartupService>(first);
            services.AddSingleton<IStartupService>(second);
        });

        Assert.True(first.Ran);
        Assert.True(second.Ran);
    }

    /// <summary>
    /// A startup service registered by an override runs too — it is in the container by the time
    /// startup happens.
    /// </summary>
    [Fact]
    public async Task AStartupServiceAddedByAnOverrideAlsoRuns() {
        var startup = new RecordingStartupService();

        await using var application = FromApplicationModule(
            overrides: (_, services) => services.AddSingleton<IStartupService>(startup));

        Assert.True(startup.Ran);
    }

    #endregion

    #region disposal

    /// <summary>
    /// Registered by type, not by instance: the container only disposes what it constructed, so an
    /// <c>AddSingleton(new Thing())</c> would prove nothing about whether the provider was disposed.
    /// </summary>
    [Fact]
    public async Task DisposingDisposesSingletonsTheContainerOwns() {
        var application = FromApplicationModule(
            (_, services) => services.AddSingleton<TrackedDisposable>());

        var disposable = application.Provider.GetRequiredService<TrackedDisposable>();

        Assert.False(disposable.Disposed);

        await application.DisposeAsync();

        Assert.True(disposable.Disposed);
    }

    #endregion

    /// <summary>
    /// Two constructors, near-identical bodies. This fails when one is edited and the other is not.
    /// </summary>
    [Fact]
    public async Task BothModuleShapesProduceTheSameWiring() {
        var startup = new RecordingStartupService();
        var otherStartup = new RecordingStartupService();

        await using var fromApplication = FromApplicationModule(
            (_, services) => {
                services.AddSingleton<IGreetingService, RealGreetingService>();
                services.AddSingleton<IStartupService>(startup);
            },
            (_, services) => services.AddSingleton<IGreetingService>(new StubGreeting()));

        await using var fromDependency = FromDependencyModule(
            services => {
                services.AddSingleton<IGreetingService, RealGreetingService>();
                services.AddSingleton<IStartupService>(otherStartup);
            },
            (_, services) => services.AddSingleton<IGreetingService>(new StubGreeting()));

        Assert.Equal(
            fromApplication.Provider.GetRequiredService<IGreetingService>().Greet("world"),
            fromDependency.Provider.GetRequiredService<IGreetingService>().Greet("world"));

        Assert.Equal(startup.Ran, otherStartup.Ran);
        Assert.True(startup.Ran);

        Assert.NotNull(fromApplication.Provider.GetRequiredService<ILogger<TestApplicationTests>>());
        Assert.NotNull(fromDependency.Provider.GetRequiredService<ILogger<TestApplicationTests>>());
    }

    private sealed class StubGreeting : IGreetingService {
        public string Greet(string name) => $"stub hello {name}";
    }

    private sealed class RecordingStartupService : IStartupService {
        public bool Ran { get; private set; }

        public Task<bool> Startup(IServiceProvider provider) {
            Ran = true;

            return Task.FromResult(true);
        }
    }

    private sealed class TrackedDisposable : IDisposable {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    #region the environment is reachable under both interfaces

    /// <summary>
    /// The module system reads <see cref="IModuleEnvironment"/> while it is deciding what to
    /// register, so a test host that registers the environment under
    /// <see cref="IHardenedEnvironment"/> alone leaves <c>[IfEnvironment]</c> answering against
    /// <c>ASPNETCORE_ENVIRONMENT</c> - <c>Production</c> - while the application under test says
    /// <c>development</c>. Registration under both is what makes the two agree.
    /// </summary>
    [Theory]
    [InlineData("staging")]
    [InlineData("development")]
    public void TheModuleSystemSeesTheEnvironmentTheTestWasGiven(string environmentName) {
        var environment = new EnvironmentImpl(environmentName);

        var application = new TestApplication(
            new ApplicationModule((_, _) => { }), "test", environment, null);

        Assert.Same(environment, application.Provider.GetRequiredService<IModuleEnvironment>());
        Assert.Equal(
            environmentName,
            application.Provider.GetRequiredService<IModuleEnvironment>().EnvironmentName);
    }

    /// <summary>
    /// Both constructors register the environment, and only one of them being fixed is exactly the
    /// drift <see cref="BothModuleShapesProduceTheSameWiring"/> exists to catch.
    /// </summary>
    [Fact]
    public void BothModuleShapesReachTheEnvironmentUnderBothInterfaces() {
        foreach (var provider in new[] {
                     FromApplicationModule().Provider, FromDependencyModule().Provider
                 }) {
            Assert.Same(
                provider.GetRequiredService<IHardenedEnvironment>(),
                provider.GetRequiredService<IModuleEnvironment>());
        }
    }

    #endregion
}
