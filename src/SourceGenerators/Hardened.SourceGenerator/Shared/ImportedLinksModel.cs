using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// A links type belonging to a module this entry point imports.
/// </summary>
/// <remarks>
/// <para>
/// Links are generated per module, so an application that keeps its routes in a library has an
/// <c>ApplicationLinks</c> with nothing on it and a <c>CatalogLibraryLinks</c> carrying every
/// route it serves. A view's generated <c>Links</c> property is typed as the former, so
/// <c>@Links.Book.ById(id)</c> did not compile and the build-time link guarantee was unavailable
/// to exactly the applications that split into libraries - which is the arrangement
/// <c>[BasePath]</c> on a module exists to support.
/// </para>
/// <para>
/// Resolved from the compilation rather than assumed. An imported module only has a links type if
/// its assembly was compiled with the web generator, and most are not - <c>[KestrelRuntime]</c>
/// and <c>[HardenedStaticContent]</c> are modules with no routes and no links type - so emitting
/// a property per import without checking would name types that do not exist.
/// </para>
/// </remarks>
public record ImportedLinksModel(string PropertyName, ITypeDefinition LinksType) {

    /// <summary>
    /// The imported modules that have links, in a stable order.
    /// </summary>
    /// <remarks>
    /// Ordered by property name so the generated file does not change when the attributes on the
    /// entry point are reordered, which would otherwise recompute everything downstream.
    /// </remarks>
    public static IReadOnlyList<ImportedLinksModel> Read(
        GeneratorSyntaxContext syntaxContext,
        ClassDeclarationSyntax entryPoint,
        IReadOnlyList<AttributeModel> attributes) {
        var entryPointName = entryPoint.Identifier.Text;

        List<ImportedLinksModel>? found = null;

        foreach (var attribute in attributes) {
            var moduleName = ModuleName(attribute.TypeDefinition.Name);

            // The entry point imports itself in no useful sense, and its own links are already the
            // type this property would hang off.
            if (moduleName == entryPointName) {
                continue;
            }

            // Nested in the module rather than named after it, so the C# spelling is a dot and the
            // metadata spelling is a plus. GetTypeByMetadataName takes the second and silently finds
            // nothing when handed the first, which would read as "that module publishes no links".
            var linksName = moduleName + ".Links";
            var metadataName = attribute.TypeDefinition.Namespace + "." + moduleName + "+Links";

            // The one question that cannot be answered from syntax: does that module actually
            // publish links?
            if (syntaxContext.SemanticModel.Compilation.GetTypeByMetadataName(metadataName) == null) {
                continue;
            }

            found ??= new List<ImportedLinksModel>();

            found.Add(new ImportedLinksModel(
                moduleName,
                TypeDefinition.Get(attribute.TypeDefinition.Namespace, linksName)));
        }

        if (found == null) {
            return Array.Empty<ImportedLinksModel>();
        }

        found.Sort((left, right) =>
            string.Compare(left.PropertyName, right.PropertyName, StringComparison.Ordinal));

        return found;
    }

    /// <summary>
    /// The module behind an attribute, which is its name without the <c>Attribute</c> suffix.
    /// </summary>
    /// <remarks>
    /// The generated companion of a module class is named after it, so this inverts the one naming
    /// rule the module system has. Both spellings reach here depending on how the attribute was
    /// written, which is why the suffix is trimmed rather than assumed present.
    /// </remarks>
    private static string ModuleName(string attributeTypeName) =>
        attributeTypeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeTypeName.Substring(0, attributeTypeName.Length - "Attribute".Length)
            : attributeTypeName;
}
