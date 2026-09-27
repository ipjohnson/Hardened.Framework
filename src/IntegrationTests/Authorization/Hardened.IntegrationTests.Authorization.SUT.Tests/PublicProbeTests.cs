using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Web.Runtime.Health;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.Authorization.SUT.Tests;

/// <summary>
/// The health probes under default-deny: refused to a caller who presents nothing, which is what a
/// load balancer is, unless the application opens them.
/// </summary>
public class PublicProbeTests
{
    [ModuleTest]
    public async Task UnderDefaultDenyAProbeRefusesAnAnonymousCaller(ITestWebApp testWebApp)
    {
        (await testWebApp.Get("/health/live")).Assert.Unauthorized();
    }

    [ModuleTest]
    [OpenProbes]
    public async Task AllowAnonymousOpensBothProbes(ITestWebApp testWebApp)
    {
        (await testWebApp.Get("/health/live")).Assert.Ok();
        (await testWebApp.Get("/health/ready")).Assert.Ok();
    }

    /// <summary>The registration an application writes to open its probes.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    private sealed class OpenProbesAttribute : Attribute, ITestServiceSetupAttribute
    {
        public void SetupServiceCollection(
            ITestMethodContext testMethod,
            IServiceCollection serviceCollection
        ) => serviceCollection.AddSingleton(new HealthCheckConfiguration { AllowAnonymous = true });
    }
}
