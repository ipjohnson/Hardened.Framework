using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Cors;
using Hardened.Web.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Cors;

/// <summary>
/// Whether the CORS filter is in the pipeline at all.
///
/// <para>
/// <c>CorsFilterTests</c> covers what the filter does once it runs. This covers the decision one
/// level up, which is opt-in by configuration: an application that allowed no origin must not have
/// the filter installed, because a filter that is present answers every <c>OPTIONS</c> request
/// with 204 and short-circuits the chain whether or not any origin was allowed.
/// </para>
///
/// <para>
/// The startup service itself is <c>internal</c>, so it is reached the way an application reaches
/// it — through <c>HardenedWebModule</c>'s registrations.
/// </para>
/// </summary>
public class CorsStartupServiceTests {

    /// <summary>
    /// Builds the module's registrations, replaces the environment-derived CORS configuration with
    /// <paramref name="configuration"/>, and returns the provider along with the middleware
    /// service the startup services will install into.
    /// </summary>
    private static (IServiceProvider provider, IMiddlewareService middleware) Application(
        CorsConfiguration configuration) {
        var services = new ServiceCollection();

        new HardenedWebModule().ConfigureServices(services);

        var middleware = Substitute.For<IMiddlewareService>();

        // Registered after the module, so it wins the resolve: the module's own registration reads
        // the process environment, which a test must not depend on.
        services.AddSingleton(configuration);
        services.AddSingleton(middleware);

        return (services.BuildServiceProvider(), middleware);
    }

    private static async Task RunStartup(IServiceProvider provider) {
        foreach (var startupService in provider.GetServices<IStartupService>()) {
            await startupService.Startup(provider);
        }
    }

    [Fact]
    public void TheWebModuleRegistersAStartupServiceForCors() {
        var services = new ServiceCollection();

        new HardenedWebModule().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IStartupService));
    }

    /// <summary>
    /// No allowed origin, no filter. Installing it anyway would turn every preflight into a 204
    /// that never reaches a handler, in an application that never asked for CORS.
    /// </summary>
    [Fact]
    public async Task NoAllowedOriginInstallsNoMiddleware() {
        var (provider, middleware) = Application(new CorsConfiguration());

        await RunStartup(provider);

        middleware.DidNotReceive().Use(Arg.Any<Func<IExecutionContext, IExecutionFilter>>());
    }

    [Fact]
    public async Task AnAllowedOriginInstallsTheMiddleware() {
        var configuration = new CorsConfiguration();

        configuration.AllowOrigin("https://app.example.com");

        var (provider, middleware) = Application(configuration);

        await RunStartup(provider);

        middleware.Received(1).Use(Arg.Any<Func<IExecutionContext, IExecutionFilter>>());
    }

    /// <summary>
    /// What was installed is the CORS filter, resolved from the container rather than constructed
    /// on the spot — so it carries the same configuration the decision was made from.
    /// </summary>
    [Fact]
    public async Task TheInstalledMiddlewareIsTheContainersCorsFilter() {
        var configuration = new CorsConfiguration();

        configuration.AllowOrigin("https://app.example.com");

        var (provider, middleware) = Application(configuration);

        Func<IExecutionContext, IExecutionFilter>? installed = null;

        middleware.Use(Arg.Do<Func<IExecutionContext, IExecutionFilter>>(func => installed = func));

        await RunStartup(provider);

        Assert.NotNull(installed);
        Assert.Same(provider.GetRequiredService<CorsFilter>(), installed!(Substitute.For<IExecutionContext>()));
    }

    /// <summary>
    /// The module's own CORS configuration is built from the process environment, which is the
    /// only way an operator can allow an origin without a code change.
    /// </summary>
    [Fact]
    public void TheModulesCorsConfigurationIsLoadedFromTheEnvironment() {
        var previous = Environment.GetEnvironmentVariable(CorsConfiguration.DefaultEnvironmentVariable);

        Environment.SetEnvironmentVariable(
            CorsConfiguration.DefaultEnvironmentVariable, "https://from-environment.example.com");

        try {
            var services = new ServiceCollection();

            new HardenedWebModule().ConfigureServices(services);

            var configuration = services.BuildServiceProvider().GetRequiredService<CorsConfiguration>();

            Assert.True(configuration.IsOriginAllowed("https://from-environment.example.com"));
        }
        finally {
            Environment.SetEnvironmentVariable(CorsConfiguration.DefaultEnvironmentVariable, previous);
        }
    }

    /// <summary>
    /// The filter is a singleton, so the instance the middleware installs is the one every request
    /// runs through rather than a new one per resolve.
    /// </summary>
    [Fact]
    public void TheCorsFilterIsASingleton() {
        var (provider, _) = Application(new CorsConfiguration());

        Assert.Same(provider.GetRequiredService<CorsFilter>(), provider.GetRequiredService<CorsFilter>());
    }
}
