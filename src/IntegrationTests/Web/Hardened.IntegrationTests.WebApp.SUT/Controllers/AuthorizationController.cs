using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Handlers carrying authorization attributes, so the whole path can be exercised through the real
/// pipeline: the attribute reaching handler metadata, the filter provider folding it, the filter
/// refusing, and the refusal reaching the wire as a status and a challenge.
/// </summary>
/// <remarks>
/// Grants arrive via <c>X-Test-Grants</c>, which the test principal middleware turns into a
/// principal. Nothing here validates a credential; that is a later phase.
/// </remarks>
[BasePath("/authorization")]
public class AuthorizationController {

    /// <summary>No attribute at all, which is public while nothing has opted in.</summary>
    [Get("/unguarded")]
    public string Unguarded() => "unguarded";

    /// <summary>Explicitly public.</summary>
    [Get("/open")]
    [AllowAnonymous]
    public string Open() => "open";

    [Get("/pets")]
    [AuthorizeGrants("pets:read")]
    public string Pets() => "pets";

    /// <summary>Both grants, which is what one requirement object in a specification means.</summary>
    [Get("/pets-manage")]
    [AuthorizeGrants("pets:read", "pets:write")]
    public string Manage() => "managed";

    /// <summary>
    /// Two alternatives, which is what the outer list of a specification's <c>security</c> means.
    /// </summary>
    [Get("/either")]
    [AuthorizeGrants("pets:read")]
    [AuthorizeGrants("admin:*")]
    public string Either() => "either";
}
