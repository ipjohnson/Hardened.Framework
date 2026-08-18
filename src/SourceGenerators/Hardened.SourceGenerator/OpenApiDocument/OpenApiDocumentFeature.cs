using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// Whether an entry point asked for its generated OpenAPI document, and where it wants it served.
/// </summary>
/// <remarks>
/// Read from a facet rather than from the marker's name, on the same terms as
/// <c>TemplateBaseGenerator</c>: an application that wants the document at another path declares its
/// own marker carrying <c>[OpenApiDocumentPath]</c> and needs no change here. A marker with no such
/// facet is some other kind of feature and is passed over, which is what lets one attribute name
/// serve every optional feature.
/// </remarks>
internal static class OpenApiDocumentFeature {

    /// <summary>The facet naming where the document is served from.</summary>
    private const string PathFacet = "OpenApiDocumentPath";

    /// <summary>
    /// The path this entry point serves its document at, or null if it enabled no such feature.
    /// </summary>
    public static string? Path(EntryPointSelector.Model appModel) {
        foreach (var feature in appModel.EnabledFeatures) {
            var path = feature.Facet(PathFacet)?.Value;

            if (!string.IsNullOrEmpty(path)) {
                return path;
            }
        }

        return null;
    }
}
