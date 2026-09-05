using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A startup service runs once per test container.
/// </summary>
/// <remarks>
/// Until 2026-09-05 it ran twice. <c>[HardenedTestEntryPoint]</c> runs the registered services
/// through <c>ApplicationLogic.Start</c>, which guards a provider against a second run, and
/// <c>[WebTesting]</c> looped them again on its own with no guard; the runner awaits both, in an
/// order that is a sort rather than a declaration. The chain carried <c>AuthenticationMiddleware</c>
/// and <c>CorsFilter</c> twice, behind the handler, and the authorization filter provider was
/// registered twice. Nothing asserted on it, so nothing noticed.
/// </remarks>
public class StartupServicesRunOnceTests {

    [HardenedTest]
    [CountingStartupService]
    public void AStartupServiceRunsOncePerContainer(CountingStartupService service) {
        Assert.Equal(1, service.Runs);
    }

    /// <summary>The chain the services composed still answers, with the handler behind them.</summary>
    [HardenedTest]
    [CountingStartupService]
    public async Task TheChainAnswersAfterTheOneRun(CountingStartupService service, ITestWebApp app) {
        var response = await app.Get("/verbs/item/1");

        response.Assert.Ok();
        Assert.Equal("got:1", response.Deserialize<string>());
        Assert.Equal(1, service.Runs);
    }
}

public sealed class CountingStartupService : IStartupService {
    private int _runs;

    public int Runs => _runs;

    public Task<bool> Startup(IServiceProvider rootProvider) {
        Interlocked.Increment(ref _runs);

        return Task.FromResult(true);
    }
}

/// <summary>Registers <see cref="CountingStartupService"/> beside the application's own startup services.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CountingStartupServiceAttribute : Attribute, IHardenedTestDependencyRegistrationAttribute {

    public void RegisterDependencies(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<CountingStartupService>();
        serviceCollection.AddSingleton<IStartupService>(sp => sp.GetRequiredService<CountingStartupService>());
    }
}
