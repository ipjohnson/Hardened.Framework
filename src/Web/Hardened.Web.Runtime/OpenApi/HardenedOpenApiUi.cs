using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Serves an OpenAPI reference page at <c>/docs</c>.
///
/// <code>
/// [HardenedModule]
/// [HardenedWebModule]
/// [Enable&lt;HardenedOpenApiDocument&gt;]              // embeds and serves /openapi.json
/// [HardenedOpenApiUi(Title = "Contoso Orders")]   // adds the page that reads it
/// [AspNetCoreRuntime]
/// public partial class Application { }
/// </code>
///
/// <para>
/// Two attributes because they are two decisions. The document is worth serving on its own - it is
/// what a client generator consumes - and the page is worthless without it. Enabling the document
/// and not the page is an ordinary arrangement; the reverse is not.
/// </para>
///
/// <para>
/// <b>A route rather than a provider, which is the whole design.</b> Authorization conventions are
/// applied in <c>ExecutionHelper.CreateFilterArray</c>, ahead of the global filter registry, and
/// every generated handler funnels through it. A provider building its own execution chain - which
/// is what the health endpoints and <see cref="OpenApiDocumentProvider"/> do - never reaches it, so
/// none of them is visible to an <c>IAuthorizationConvention</c>. This is, so a convention can gate
/// the page without the framework shipping a switch for it.
/// </para>
///
/// <para>
/// <b>There is deliberately no <c>[AllowAnonymous]</c> on the handler.</b> That is the one thing a
/// convention cannot narrow. Without it the page inherits default-deny where default-deny is on,
/// stays public where no authorization is configured, and is gate-able by convention everywhere
/// else - three behaviours and nothing to configure.
/// </para>
///
/// <para>
/// <b>Nothing is registered unless this attribute is applied</b>, which is what lets the page live
/// in the core web package rather than in one of its own. It is a few hundred bytes of markup and
/// two small classes; the UI itself is never carried here.
/// </para>
///
/// <para>
/// <b>The path is fixed.</b> Route paths are compile-time constants read from attributes, so it
/// cannot be a property. An application's <c>[BasePath]</c> still applies, and one that wants the
/// page elsewhere declares its own route there - which shadows this, because providers are consulted
/// in reverse registration order. <see cref="DocumentPath"/> is a property because it is a string
/// rendered into the page rather than a route.
/// </para>
/// </summary>
[HardenedModule]
[WebLibrary]
public partial class HardenedOpenApiUi : IServiceCollectionConfiguration {

    /// <summary>The page title. Defaults to something true of any API.</summary>
    /// <remarks>
    /// Nullable, and so is every property here, because that is what makes a default survive.
    /// DependencyModules generates the module attribute with every property defaulting to
    /// <c>default(T)</c> and copies it onto the module - guarded by a null check <em>only for a
    /// nullable one</em>. A non-nullable <c>string</c> property would therefore be assigned null by
    /// <c>[HardenedOpenApiUi]</c> written with no arguments, and the initializer written here would
    /// never be seen.
    /// </remarks>
    public string? Title { get; set; } = DefaultTitle;

    /// <summary>
    /// Where the page fetches the document from. Matches what
    /// <c>[Enable&lt;HardenedOpenApiDocument&gt;]</c> serves.
    /// </summary>
    public string? DocumentPath { get; set; } = DefaultDocumentPath;

    /// <summary>
    /// The reference UI, version-pinned, loaded by the browser rather than carried here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pinned because <see cref="ScriptIntegrity"/> is a hash of exact bytes, so a floating tag
    /// could not carry one. The cost is that the UI version moves on this package's release cadence;
    /// the benefit is that a swapped or compromised CDN asset fails to execute rather than running.
    /// Override both together or neither.
    /// </para>
    /// <para>
    /// A consumer behind a VPC, or with a <c>script-src</c> policy that will not name a CDN, points
    /// this at a copy the application serves itself - its own <c>wwwroot</c> is enough, and
    /// <c>StaticContentHandler</c> already serves it - and clears <see cref="ScriptIntegrity"/>,
    /// which same-origin does not need.
    /// </para>
    /// </remarks>
    public string? ScriptUrl { get; set; } = DefaultScriptUrl;

    /// <summary>
    /// The subresource integrity hash for <see cref="ScriptUrl"/>.
    /// </summary>
    /// <remarks>
    /// Set it to the empty string, not null, to state that there is none - which is the right answer
    /// for a same-origin copy. Null is indistinguishable from "not set", for the reason
    /// <see cref="Title"/> gives, and so leaves the default in place.
    /// </remarks>
    public string? ScriptIntegrity { get; set; } = DefaultScriptIntegrity;

    public const string DefaultTitle = "API Reference";

    public const string DefaultDocumentPath = "/openapi.json";

    public const string DefaultScriptUrl =
        "https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.65.1/dist/browser/standalone.js";

    public const string DefaultScriptIntegrity =
        "sha384-G6dkutu2k5IYVyNESLoFIpgaHx38IJTZ/HhrwN0fecTle9te75y8Kru3rJEJ0ZJV";

    public void ConfigureServices(IServiceCollection services) {
        // Coalesced rather than trusted. The properties are nullable so that an unset one keeps its
        // default, which leaves null reachable here for anyone constructing the module directly.
        services.AddSingleton<IOpenApiUiConfiguration>(
            new OpenApiUiConfiguration(
                Title ?? DefaultTitle,
                DocumentPath ?? DefaultDocumentPath,
                ScriptUrl ?? DefaultScriptUrl,
                ScriptIntegrity));
    }
}
