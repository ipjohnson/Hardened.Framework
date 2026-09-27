namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Who may read the published document and its reference page.
/// </summary>
/// <remarks>
/// Registered by the application, the way <see cref="Health.HealthCheckConfiguration"/> is:
/// <code>
/// services.AddSingleton(new OpenApiDocumentConfiguration { AllowAnonymous = true });
/// </code>
/// </remarks>
public sealed class OpenApiDocumentConfiguration
{
    /// <summary>
    /// Serve the document and the reference page to any caller, under <c>[RequireAuthorization]</c>
    /// as well.
    /// </summary>
    /// <remarks>
    /// Off, which leaves both under the application's posture: public where nothing is required,
    /// and refused under default-deny. On, they carry <c>[AllowAnonymous]</c>, which also means a
    /// convention can no longer narrow them.
    /// </remarks>
    public bool AllowAnonymous { get; set; }
}
