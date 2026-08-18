namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Turns one page's configuration into its model.
/// </summary>
/// <remarks>
/// <para>
/// No route attribute: the path is configuration, not a compile-time constant, so
/// <see cref="OpenApiUiProvider"/> declares the route and hands this the configuration that goes
/// with it. Stateless, and registered as a singleton for that reason.
/// </para>
/// <para>
/// It is a controller rather than the provider doing this inline so the page has the same shape as
/// any other handler - a model in, an output writing it - which is what
/// <c>ExecutionHelper.StandardFilterEmptyParameters</c> is built to run and what makes the filter
/// chain, conventions included, apply to it unchanged.
/// </para>
/// </remarks>
public class OpenApiUiController {

    public OpenApiUiModel Index(IOpenApiUiConfiguration configuration) =>
        new(configuration.Title,
            configuration.DocumentPath,
            configuration.ScriptUrl,
            configuration.ScriptIntegrity);
}
