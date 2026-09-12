using Hardened.Requests.Abstract.Authorization;
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
/// </para>
/// <para>
/// Half the handlers name <see cref="PetsOAuth"/> and half do not, which is the distinction the
/// document turns on. A grant with no scheme beside it publishes nothing - a requirement has to
/// reference a declared scheme - so <see cref="Pets"/> publishes <c>security</c>, a 401 and the
/// challenge header, while <see cref="Unstated"/> requires the same grant at run time and
/// publishes a 403 alone. Both are supported and only one is describable, and the suite covers
/// each because the difference is invisible from the handler.
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
    [Authorize<PetsOAuth>]
    [AuthorizeGrants("pets:read")]
    public string Pets() => "pets";

    /// <summary>The same grant with no scheme named, which is guarded and undescribable.</summary>
    [Get("/pets-unstated")]
    [AuthorizeGrants("pets:read")]
    public string Unstated() => "unstated";

    /// <summary>Both grants, which is what one requirement object in a specification means.</summary>
    [Get("/pets-manage")]
    [Authorize<PetsOAuth>]
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
    [Authorize<PetsOAuth>]
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
    [Authorize<PetsOAuth>]
    [RequiresPetWrite]
    public string Derived() => "derived";
}

/// <summary>
/// A grant named once and spelled as a type everywhere it is required.
/// </summary>
public sealed class RequiresPetWriteAttribute : AuthorizeGrantsAttribute {
    public RequiresPetWriteAttribute() : base("pets:read", "pets:write") { }
}

/// <summary>
/// The scheme this application's guarded operations name.
/// </summary>
/// <remarks>
/// <para>
/// OAuth2 rather than bearer because it is the kind that carries scopes: grants required beside
/// an OAuth2 scheme become the requirement's scope list, so <c>pets:read</c> reaches the document
/// as something a reader can act on rather than as a 403 with no explanation. Under an HTTP
/// scheme the same operations would publish "authenticated via this scheme" and nothing more.
/// </para>
/// <para>
/// The transport does not match and is not meant to. Callers here send <c>X-Test-Grants</c> and
/// nothing validates a token; what the type declares is the shape of the published contract, which
/// is the whole of what a scheme is to the generator - it reads the attribute and cannot run the
/// code that establishes a caller.
/// </para>
/// </remarks>
[OAuth2AuthenticationScheme(
    OAuth2Flow.ClientCredentials,
    TokenUrl = "https://example.invalid/token",
    Description = "The scheme the integration fixture's guarded operations name.")]
public sealed class PetsOAuth : IAuthenticationScheme;
