using CSharpAuthor;
using Hardened.Idl.Models;

namespace Hardened.Idl.Emitters;

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

        var text = definition.AddProperty(ReadOnlySpanOfByte, "DocumentGZip");

        // Gzipped bytes rather than a verbatim string literal. A C# string literal lives in the
        // assembly's #US heap as UTF-16, so it cost two bytes per ASCII character - which is what
        // kept EmbedDocument off by default, because a large description could exceed the
        // user-string limit on its own. An RVA blob is a different heap and about an eighth the
        // size. Fidelity is untouched: gzip round-trips the source text exactly, comments and line
        // endings included, which is the whole point of embedding the input rather than re-emitting
        // it.
        text.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        text.Set = null;
        text.Get.LambdaSyntax = true;
        text.Get.AddCode(GZipLiteral.Write(document));
        text.Comment = "The document this application was generated from, gzip-compressed.";

        return definition;
    }

    private static ITypeDefinition ReadOnlySpanOfByte =>
        new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, "System", "ReadOnlySpan",
            new[] { TypeDefinition.Get(typeof(byte)) });

    public static string TypeName(string fileName) =>
        NamingHelper.SpecificationTypeName(fileName);

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

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
