using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// A handler that reads the time from the container rather than from <c>DateTimeOffset.UtcNow</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every arm of the last two trials hand-rolled an <c>IClock</c> and a <c>SystemClock</c> to drive
/// expiry from a test. <c>TimeProvider</c> is the BCL's own abstraction for it and the core module
/// registers <c>TimeProvider.System</c>, so an application injects it and a test substitutes it.
/// </para>
/// <para>
/// <c>[FromServices]</c> rather than a bare parameter, because <c>TimeProvider</c> is an abstract
/// class and the convention that resolves a parameter from the container reads interfaces - a
/// concrete type falls through to the body branch.
/// </para>
/// </remarks>
[BasePath("/clock")]
public class ClockController {

    [Get("/now")]
    public DateTimeOffset Now([FromServices] TimeProvider timeProvider) => timeProvider.GetUtcNow();
}
