using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// What a handler declaring <c>[Output&lt;T&gt;]</c> answers with, for the document.
/// </summary>
/// <remarks>
/// <para>
/// Read from the entry point's template markers rather than from the output type, because the
/// output type is another generator's product and invisible here. A marker declares
/// <c>[TemplateContentType]</c>, which is what the generated base writes onto the response, so a
/// marker's facet and what the wire carries are one value read in two places.
/// </para>
/// <para>
/// <c>text/html</c> when the entry point enables no template engine, or more than one. An
/// application with two engines cannot be resolved from here - which marker's base a given view
/// derives from is exactly the thing that cannot be seen - and a handler in that position states
/// its own with <c>[Produces]</c>, which is read ahead of this. The fallback is the media type
/// every engine that ships with the framework produces, and it is a guess only where a second
/// engine producing something else is enabled.
/// </para>
/// </remarks>
internal static class TemplateOutputFeature
{
    /// <summary>The facet naming what a marker's templates produce.</summary>
    private const string ContentTypeFacet = "TemplateContentType";

    private const string Html = "text/html";

    /// <summary>
    /// The media type an output writes, for an entry point that enabled one template engine.
    /// </summary>
    public static string ContentType(EntryPointSelector.Model appModel)
    {
        string? only = null;

        foreach (var feature in appModel.EnabledFeatures)
        {
            var declared = feature.Facet(ContentTypeFacet)?.Value;

            if (string.IsNullOrEmpty(declared))
            {
                continue;
            }

            if (only != null)
            {
                return Html;
            }

            only = declared;
        }

        return only ?? Html;
    }
}
