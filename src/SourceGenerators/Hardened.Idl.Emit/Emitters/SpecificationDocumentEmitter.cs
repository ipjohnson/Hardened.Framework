using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.OpenApi.SourceGenerator.Emitters;

/// <summary>
/// The specification itself, embedded verbatim so the application can serve the document it was
/// built from.
/// </summary>
/// <remarks>
/// <para>
/// A specification-first application already <em>has</em> its contract - it is a build input. Any
/// document it served that had been through an emitter would be a second description of the same
/// thing, agreeing with the first only as far as the emitter had been taught to. Serving the source
/// text makes fidelity a property of the arrangement rather than something to keep testing:
/// descriptions, examples, security schemes, vendor extensions, the ordering, the comments in a
/// YAML file - all of it survives, including everything Hardened's own model does not represent.
/// </para>
/// <para>
/// Emitted from the build task rather than the generator because the task is the only thing that
/// ever reads the file. The generator sees a normalised model and has no file system to go back to.
/// </para>
/// <para>
/// The content type travels with it. A YAML specification is served as YAML: converting it to JSON
/// to fit a conventional <c>/openapi.json</c> would put exactly the emitter back in the path that
/// this exists to remove.
/// </para>
/// </remarks>
internal static class SpecificationDocumentEmitter {

    public static ClassDefinition Emit(
        NamespaceDefinition container, ServiceSpecModel model, string document, string specPath) {
        var definition = container.AddClass(TypeName(model.FileName));

        definition.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        definition.Comment = "The OpenAPI document this application was generated from, verbatim.";

        var contentType = definition.AddField(typeof(string), "ContentType");

        contentType.Modifiers |= ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        contentType.InitializeValue =
            new CodeOutputComponent(Quote(ContentType(specPath))) { Indented = false };

        var text = definition.AddField(typeof(string), "Document");

        // static readonly rather than const: CSharpAuthor has no const modifier, and a const of
        // this size would be inlined into every assembly that referenced it.
        text.Modifiers |= ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        text.InitializeValue = new CodeOutputComponent(Verbatim(document)) { Indented = false };

        return definition;
    }

    public static string TypeName(string fileName) =>
        NamingHelper.ToPascalCase(fileName) + "Specification";

    /// <summary>
    /// From the extension, since that is the only thing that says which of the two interchangeable
    /// forms the author wrote. Unknown extensions are JSON because that is what an OpenAPI document
    /// is unless it says otherwise.
    /// </summary>
    private static string ContentType(string specPath) {
        var extension = Path.GetExtension(specPath);

        return string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)
            ? "application/yaml"
            : "application/json";
    }

    /// <summary>
    /// A verbatim literal, so the only character needing an escape is the quote.
    /// </summary>
    /// <remarks>
    /// The alternative is a regular literal with every newline, tab and backslash escaped, which
    /// for a YAML document of any size is a single unreadable line and a much larger surface for an
    /// escaping mistake to hide in. A verbatim literal also carries the document's own line endings
    /// through unchanged, which is what "verbatim" has to mean if the served bytes are to match the
    /// file.
    /// </remarks>
    private static string Verbatim(string document) =>
        "@\"" + document.Replace("\"", "\"\"") + "\"";

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
