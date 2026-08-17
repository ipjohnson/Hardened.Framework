using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Authorization.SUT.Controllers;

/// <summary>
/// Handlers in an application that requires authorization.
/// </summary>
[BasePath("/guarded")]
public class GuardedController {

    /// <summary>
    /// Says nothing, on purpose. This is the case the whole fixture exists for: under
    /// <c>[RequireAuthorization]</c> a handler that declares nothing is refused rather than public,
    /// and no unit test can show that the whole chain - attribute, generated registration, amended
    /// configuration, startup service, filter provider, filter - actually reaches that answer.
    /// </summary>
    [Get("/implicit")]
    public string Implicit() => "implicit";

    /// <summary>The opt-out, which is what makes a health check or a login route expressible.</summary>
    [Get("/open")]
    [AllowAnonymous]
    public string Open() => "open";

    [Get("/pets")]
    [AuthorizeGrants("pets:read")]
    public string Pets() => "pets";

    [Get("/pets-manage")]
    [AuthorizeGrants("pets:read", "pets:write")]
    public string Manage() => "managed";
}

/// <summary>
/// The opt-out written once for a whole controller.
/// </summary>
/// <remarks>
/// A handler's filters carry its controller's attributes as well as its own, so this covers every
/// route below it - the same reading the build diagnostic uses.
/// </remarks>
[BasePath("/public")]
[AllowAnonymous]
public class PublicController {

    [Get("/health")]
    public string Health() => "healthy";

    [Get("/version")]
    public string Version() => "1.0";
}
