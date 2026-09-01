using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// <c>[EnvironmentName]</c> on a test method against a registration gated
/// <c>[IfEnvironment]</c> on the same name, through the real xunit v3 entry point.
/// </summary>
/// <remarks>
/// The second trial's arm B reported this exact pairing not working: the module system read a
/// default environment however the test was annotated, so an <c>[IfEnvironment]</c>-gated
/// registration could not be exercised from a test at all. The mechanism it described is fixed -
/// the harness registers the test's environment under <see cref="IModuleEnvironment"/> as well as
/// <see cref="IHardenedEnvironment"/> - but every test of that fix drives the setup pipeline by
/// hand, and the ordering between environment registration and module application inside the
/// xunit extension model was exactly the part a hand-driven test cannot see. These two run through
/// <c>[HardenedTest]</c> itself, against the SUT's own generated module, which is what the arm did.
/// </remarks>
public class EnvironmentGatedRegistrationTests {

    [HardenedTest]
    [EnvironmentName("environment-gated")]
    public void ARegistrationGatedOnTheTestsEnvironmentResolves(IApplicationRoot application) {
        var service = application.Provider.GetService<IEnvironmentGatedService>();

        Assert.NotNull(service);
        Assert.Equal("environment-gated", service.Environment);
    }

    /// <summary>
    /// The gate has to hold in the other direction, or the test above passes because the
    /// registration is unconditional and the condition was never compiled in.
    /// </summary>
    [HardenedTest]
    public void TheSameRegistrationIsAbsentUnderTheDefaultEnvironment(IApplicationRoot application) {
        Assert.Null(application.Provider.GetService<IEnvironmentGatedService>());
    }
}
