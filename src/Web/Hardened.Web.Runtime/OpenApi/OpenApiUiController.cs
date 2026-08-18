using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// The reference page.
/// </summary>
public class OpenApiUiController {

    /// <summary>
    /// Renders the reference page for the document this application serves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No <c>[AllowAnonymous]</c>, deliberately - see <see cref="HardenedOpenApiUi"/>. It is the one
    /// declaration an <c>IAuthorizationConvention</c> cannot narrow, and a docs page that cannot be
    /// gated is the wrong default the first time somebody ships a private API.
    /// </para>
    /// <para>
    /// The path is a constant because a route path has to be. An application's <c>[BasePath]</c>
    /// still prefixes it, and an application that wants the page somewhere else entirely declares
    /// its own route there - which shadows this one, because providers are consulted in reverse
    /// registration order.
    /// </para>
    /// </remarks>
    [Get("/docs")]
    [Output<OpenApiUiPage>]
    public OpenApiUiModel Index(IOpenApiUiConfiguration configuration) =>
        new(configuration.Title,
            configuration.DocumentPath,
            configuration.ScriptUrl,
            configuration.ScriptIntegrity);
}
