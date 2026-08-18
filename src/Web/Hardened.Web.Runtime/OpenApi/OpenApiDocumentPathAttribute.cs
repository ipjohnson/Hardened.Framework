namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Declares that a feature marker serves the generated OpenAPI document, and where.
/// </summary>
/// <remarks>
/// <para>
/// The facet the routing generator reads. It never asks what the marker <em>is</em> - the same
/// principle <c>[TemplateBase]</c> established - so an application wanting the document at another
/// path ships its own marker and needs no generator change:
/// </para>
/// <code>
/// [OpenApiDocumentPath("/spec.json")]
/// public sealed class SpecEndpoint { }
///
/// [Enable&lt;SpecEndpoint&gt;]
/// public partial class Application { }
/// </code>
/// <para>
/// One attribute stating one fact, which is all <c>FeatureFacet</c> carries. Its presence is what
/// turns the document on; its value is where the route goes.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class OpenApiDocumentPathAttribute : Attribute {
    public OpenApiDocumentPathAttribute(string path) {
        Path = path;
    }

    /// <summary>The path the document is served from.</summary>
    public string Path { get; }
}
