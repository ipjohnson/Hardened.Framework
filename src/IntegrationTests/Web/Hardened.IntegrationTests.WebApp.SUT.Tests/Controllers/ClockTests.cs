using NSubstitute;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Movable time, without an application writing the seam for it.
/// </summary>
/// <remarks>
/// H-22. All three arms of the 0.18 trial hand-rolled an <c>IClock</c> and a <c>SystemClock</c>,
/// ten identical lines each, because nothing in the framework or the harness provided one. The
/// core module registers <c>TimeProvider.System</c> now, so an application injects the BCL's own
/// abstraction and a test substitutes it with <c>[Mock]</c> like any other singleton.
/// </remarks>
public class ClockTests {

    [HardenedTest]
    public async Task TheRegisteredClockIsTheSystemOne(ITestWebApp testWebApp) {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        var response = await testWebApp.Get("/clock/now");

        response.Assert.Ok();

        Assert.InRange(
            response.Deserialize<DateTimeOffset>(), before, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    /// <summary>
    /// A test moves time by substituting the provider, with no application-defined seam between
    /// the two.
    /// </summary>
    [HardenedTest]
    public async Task ATestCanMoveTime(ITestWebApp testWebApp, [Mock] TimeProvider timeProvider) {
        var moon = new DateTimeOffset(1969, 7, 20, 20, 17, 0, TimeSpan.Zero);

        timeProvider.GetUtcNow().Returns(moon);

        var response = await testWebApp.Get("/clock/now");

        response.Assert.Ok();

        Assert.Equal(moon, response.Deserialize<DateTimeOffset>());
    }
}
