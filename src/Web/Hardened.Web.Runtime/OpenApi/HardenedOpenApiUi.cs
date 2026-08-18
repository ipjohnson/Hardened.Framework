using DependencyModules.Runtime.Interfaces;
using DependencyModules.Runtime.Attributes;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Serves an OpenAPI reference page.
///
/// <code>
/// [HardenedModule]
/// [HardenedWebModule]
/// [Enable&lt;OpenApiDocumentPublishing&gt;]     // embeds and serves /openapi.json
/// [HardenedOpenApiUi(Title = "Contoso Orders")]
/// [AspNetCoreRuntime]
/// public partial class Application { }
/// </code>
///
/// <para>
/// Two attributes because they are two decisions. The document is worth serving on its own - it is
/// what a client generator consumes - and the page is worthless without one. Publishing the document
/// and not the page is an ordinary arrangement; the reverse is not.
/// </para>
///
/// <para>
/// <b>Install it more than once for more than one page.</b> <see cref="Equals"/> is keyed on
/// <see cref="Path"/>, so two installs at different paths both load and two at the same path collapse
/// into one - which is what a service publishing several specifications needs:
/// </para>
///
/// <code>
/// [HardenedOpenApiUi(Path = "/docs",          DocumentPath = "/openapi.json")]
/// [HardenedOpenApiUi(Path = "/docs/internal", DocumentPath = "/internal.json", Title = "Internal")]
/// public partial class Application { }
/// </code>
///
/// <para>
/// <b>Conventions apply to it.</b> The page has no generated route - a route path is a compile-time
/// constant and this one is configuration - but <see cref="OpenApiUiProvider"/> builds its chain
/// through <c>ExecutionHelper</c> rather than hand-rolling one, which is where authorization
/// conventions are applied. The health endpoints and <see cref="OpenApiDocumentProvider"/> hand-roll
/// theirs and are invisible to a convention; this is not.
/// </para>
///
/// <para>
/// <b>There is deliberately no <c>[AllowAnonymous]</c>.</b> That is the one thing a convention cannot
/// narrow. Without it the page inherits default-deny where default-deny is on, stays public where no
/// authorization is configured, and is gate-able by convention everywhere else - three behaviours and
/// nothing to configure.
/// </para>
///
/// <para>
/// <b>The UI loads from a CDN under subresource integrity</b>, so no third-party asset is carried
/// here and a swapped one fails to execute rather than running. A consumer behind a VPC, or with a
/// <c>script-src</c> policy that will not name a CDN, points <see cref="ScriptUrl"/> at a copy the
/// application already serves - its own <c>wwwroot</c> is enough - and sets
/// <see cref="ScriptIntegrity"/> to the empty string.
/// </para>
/// </summary>
[DependencyModule]
public partial class HardenedOpenApiUi : IServiceCollectionConfiguration {

    /// <summary>
    /// Where the page is served. Also this module's identity - see <see cref="Equals"/>.
    /// </summary>
    /// <remarks>
    /// Nullable, and so is every property here, because that is what makes a default survive.
    /// DependencyModules generates the module attribute with every property defaulting to
    /// <c>default(T)</c> and copies each onto the module, guarded by a null check <em>only for a
    /// nullable one</em>. A non-nullable <c>string</c> would therefore be assigned null by
    /// <c>[HardenedOpenApiUi]</c> written with no arguments, and the initializer here never seen.
    /// </remarks>
    public string? Path { get; set; } = DefaultPath;

    /// <summary>The page title.</summary>
    public string? Title { get; set; } = DefaultTitle;

    /// <summary>
    /// Where the page fetches its document from. Matches what
    /// <c>[Enable&lt;OpenApiDocumentPublishing&gt;]</c> serves, or a specification-first
    /// application's own registration.
    /// </summary>
    public string? DocumentPath { get; set; } = DefaultDocumentPath;

    /// <summary>
    /// The reference UI, version-pinned, loaded by the browser rather than carried here.
    /// </summary>
    /// <remarks>
    /// Pinned because <see cref="ScriptIntegrity"/> is a hash of exact bytes, so a floating tag could
    /// not carry one. The cost is that the UI version moves on this package's release cadence.
    /// Override both together or neither.
    /// </remarks>
    public string? ScriptUrl { get; set; } = DefaultScriptUrl;

    /// <summary>
    /// The subresource integrity hash for <see cref="ScriptUrl"/>.
    /// </summary>
    /// <remarks>
    /// Set it to the empty string, not null, to state that there is none - which is the right answer
    /// for a same-origin copy. Null is indistinguishable from "not set", for the reason
    /// <see cref="Path"/> gives, and so leaves the default in place.
    /// </remarks>
    public string? ScriptIntegrity { get; set; } = DefaultScriptIntegrity;

    public const string DefaultPath = "/docs";

    public const string DefaultTitle = "API Reference";

    public const string DefaultDocumentPath = "/openapi.json";

    public const string DefaultScriptUrl =
        "https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.65.1/dist/browser/standalone.js";

    public const string DefaultScriptIntegrity =
        "sha384-G6dkutu2k5IYVyNESLoFIpgaHx38IJTZ/HhrwN0fecTle9te75y8Kru3rJEJ0ZJV";

    /// <summary>
    /// The page this module installs, as one more request handler provider.
    /// </summary>
    /// <remarks>
    /// The configuration is captured by the provider rather than registered. Several pages may be
    /// installed, so a single <c>IOpenApiUiConfiguration</c> in the container would be whichever
    /// module ran last, and every page would render the same one.
    /// </remarks>
    public void ConfigureServices(IServiceCollection services) {
        // Stateless, and resolved once per request by the instance filter - so a singleton, and
        // Try because every installed page shares the one type.
        services.TryAddSingleton<OpenApiUiController>();

        var configuration = new OpenApiUiConfiguration(
            NormalisePath(Path ?? DefaultPath),
            Title ?? DefaultTitle,
            DocumentPath ?? DefaultDocumentPath,
            ScriptUrl ?? DefaultScriptUrl,
            ScriptIntegrity);

        services.AddSingleton<IWebExecutionRequestHandlerProvider>(
            serviceProvider => new OpenApiUiProvider(configuration, serviceProvider));
    }

    /// <summary>
    /// A request path always begins with a slash, so a page configured as <c>"docs"</c> would match
    /// nothing and report nothing.
    /// </summary>
    private static string NormalisePath(string path) =>
        path.StartsWith('/') ? path : "/" + path;

    /// <summary>
    /// Two installations are the same page when they are served from the same path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DependencyModules loads a module once per distinct value of its equality, so this is what
    /// makes several pages installable - one per specification an application publishes - while
    /// still collapsing a path installed twice into a single route rather than registering two
    /// providers that answer it.
    /// </para>
    /// <para>
    /// Keyed on the path alone rather than on the whole configuration, deliberately. Two pages at one
    /// path differing only in title is a mistake, not a request for two pages, and the second cannot
    /// be reached in any case: providers are consulted in reverse registration order, so it would
    /// simply shadow the first.
    /// </para>
    /// </remarks>
    public override bool Equals(object? obj) =>
        obj is HardenedOpenApiUi other &&
        string.Equals(
            NormalisePath(other.Path ?? DefaultPath),
            NormalisePath(Path ?? DefaultPath),
            StringComparison.Ordinal);

    public override int GetHashCode() =>
        NormalisePath(Path ?? DefaultPath).GetHashCode(StringComparison.Ordinal);
}
