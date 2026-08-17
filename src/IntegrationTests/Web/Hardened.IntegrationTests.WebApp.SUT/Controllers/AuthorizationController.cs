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
    /// Two attributes, which conjoin - so this needs everything both of them named.
    /// </summary>
    /// <remarks>
    /// Stacking narrows and never widens. Exercised end to end because the rule is only worth
    /// anything if it survives the whole path: the generator putting both attributes in metadata,
    /// the handler info conjoining them, and the filter refusing a caller holding one.
    /// </remarks>
    [Get("/stacked")]
    [AuthorizeGrants("pets:read")]
    [AuthorizeGrants("admin:*")]
    public string Stacked() => "stacked";

    /// <summary>
    /// An attribute of the application's own, deriving from <c>[AuthorizeGrants]</c>.
    /// </summary>
    /// <remarks>
    /// The hand-authored form. It reaches the pipeline the same way the framework's own attributes
    /// do - recognised by the interface it inherits rather than by its name - and it is the case a
    /// name-matching build diagnostic used to warn about while the runtime guarded it correctly.
    /// </remarks>
    [Get("/derived")]
    [RequiresPetWrite]
    public string Derived() => "derived";
}

/// <summary>
/// A grant named once and spelled as a type everywhere it is required.
/// </summary>
public sealed class RequiresPetWriteAttribute : AuthorizeGrantsAttribute {
    public RequiresPetWriteAttribute() : base("pets:read", "pets:write") { }
}
