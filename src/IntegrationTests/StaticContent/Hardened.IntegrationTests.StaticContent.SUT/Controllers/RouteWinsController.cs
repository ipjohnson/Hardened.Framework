using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.StaticContent.SUT.Controllers;

/// <summary>
/// A route at a path a file also sits at.
/// </summary>
/// <remarks>
/// The mount is an <c>IFallbackRequestHandlerProvider</c>, consulted after every ordinary provider,
/// so this must win. There is a <c>wwwroot/app.js</c> and this answers <c>/app.js</c>; nothing but
/// a fixture with both can tell the ordering apart from luck.
/// </remarks>
public class RouteWinsController {

    [Get("/app.js")]
    public string Declared() => "// declared by a route, not served from disk";
}
