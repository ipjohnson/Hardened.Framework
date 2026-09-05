using System.Collections.Immutable;
using CSharpAuthor;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// The handlers declaring <c>[CacheResponse]</c> in the modules an entry point imports, by name.
/// </summary>
/// <remarks>
/// <para>
/// <c>HRDW005</c> is asked of the compilation that hosts the runtime, because that is the one
/// whose module attributes say whether a store is registered. The handlers, in the layout the
/// template scaffolds, are in a library that compilation only references - so the question has to
/// be answered by reading the library's metadata, which is the arrangement
/// <see cref="ImportedLinksModel"/> already relies on for links.
/// </para>
/// <para>
/// Read here, at the syntax transform, while the compilation is in reach. What survives into the
/// model is a sorted list of names, comparable by value, so it keys the incremental cache like
/// everything else on the entry point.
/// </para>
/// </remarks>
public static class ImportedCachedHandlers {
    private const string CacheAttributeName = "CacheResponseAttribute";

    private const string CacheAttributeNamespace = "Hardened.Requests.Runtime.Caching";

    private const string VerbAttributeNamespace = "Hardened.Web.Runtime.Attributes";

    private static readonly string[] VerbAttributeNames = {
        "GetAttribute", "PostAttribute", "PutAttribute", "PatchAttribute", "DeleteAttribute"
    };

    /// <summary>
    /// The module attributes that bring a store, as <c>ResponseCacheStoreDiagnostics</c> names
    /// them - repeated here because that class is not compiled into every generator this file is.
    /// </summary>
    private static readonly string[] StoreModuleAttributes = {
        "HardenedMemoryResponseCacheAttribute"
    };

    /// <summary>
    /// Whether any module the attributes apply carries a store module attribute itself.
    /// </summary>
    /// <remarks>
    /// A library module that applies <c>[HardenedMemoryResponseCache]</c> registers the store for
    /// every application that imports it - and for the test harness that boots it, which is the
    /// reason the caching guide gives for putting it there. The host that imports such a library
    /// has a store, whatever its own attributes say.
    /// </remarks>
    public static bool ImportsAStore(
        GeneratorSyntaxContext syntaxContext, IReadOnlyList<AttributeModel> attributes) {
        var compilation = syntaxContext.SemanticModel.Compilation;

        foreach (var attribute in attributes) {
            var module = ModuleType(compilation, attribute);

            if (module == null) {
                continue;
            }

            foreach (var applied in module.GetAttributes()) {
                if (applied.AttributeClass != null &&
                    Array.IndexOf(StoreModuleAttributes, applied.AttributeClass.Name) >= 0) {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The module class an attribute applies: the attribute's name without its suffix, in the
    /// attribute's namespace, which is how DependencyModules names the generated companion.
    /// </summary>
    private static INamedTypeSymbol? ModuleType(Compilation compilation, AttributeModel attribute) {
        var ns = attribute.TypeDefinition.Namespace;
        var name = attribute.TypeDefinition.Name;

        if (name.EndsWith("Attribute", StringComparison.Ordinal)) {
            name = name.Substring(0, name.Length - "Attribute".Length);
        }

        var prefix = string.IsNullOrEmpty(ns) ? "" : ns + ".";

        return compilation.GetTypeByMetadataName(prefix + name);
    }

    /// <summary>
    /// <c>Type.Method</c> for every cached handler in every module the attributes apply, sorted.
    /// </summary>
    public static IReadOnlyList<string> Read(
        GeneratorSyntaxContext syntaxContext, IReadOnlyList<AttributeModel> attributes) {
        var compilation = syntaxContext.SemanticModel.Compilation;
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        List<string>? found = null;

        foreach (var attribute in attributes) {
            var assembly = ModuleAssembly(compilation, attribute);

            // The entry point's own assembly is the one the routing table already reports on, and
            // an assembly reached through two attributes is walked once.
            if (assembly == null ||
                SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly) ||
                !visited.Add(assembly)) {
                continue;
            }

            Walk(assembly.GlobalNamespace, found ??= new List<string>());
        }

        if (found == null) {
            return Array.Empty<string>();
        }

        found.Sort(StringComparer.Ordinal);

        return found;
    }

    /// <summary>
    /// The assembly declaring the module an attribute applies, or null for an attribute that is
    /// not a module's, or one whose type the compilation cannot see.
    /// </summary>
    /// <remarks>
    /// Both spellings of the name reach here depending on how the attribute was written, so both
    /// are tried; the generated companion of a module is named after the module with the suffix.
    /// </remarks>
    private static IAssemblySymbol? ModuleAssembly(Compilation compilation, AttributeModel attribute) {
        var ns = attribute.TypeDefinition.Namespace;
        var name = attribute.TypeDefinition.Name;

        var prefix = string.IsNullOrEmpty(ns) ? "" : ns + ".";

        var symbol = compilation.GetTypeByMetadataName(prefix + name) ??
                     compilation.GetTypeByMetadataName(prefix + name + "Attribute");

        return symbol?.ContainingAssembly;
    }

    private static void Walk(INamespaceSymbol ns, List<string> found) {
        foreach (var member in ns.GetMembers()) {
            switch (member) {
                case INamespaceSymbol child:
                    Walk(child, found);
                    break;

                case INamedTypeSymbol type:
                    Visit(type, found);
                    break;
            }
        }
    }

    /// <summary>
    /// A method declaring caching counts however it is routed; under a class-level declaration
    /// only the methods carrying a verb attribute do, which is what the class's declaration
    /// reaches.
    /// </summary>
    private static void Visit(INamedTypeSymbol type, List<string> found) {
        var classDeclares = DeclaresCaching(type.GetAttributes());

        foreach (var member in type.GetMembers()) {
            if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method) {
                continue;
            }

            var attributes = method.GetAttributes();

            if (DeclaresCaching(attributes) || (classDeclares && IsRoute(attributes))) {
                found.Add(type.Name + "." + method.Name);
            }
        }

        foreach (var nested in type.GetTypeMembers()) {
            Visit(nested, found);
        }
    }

    /// <summary>
    /// Either form: the generic attribute's symbol has the same name and namespace, with an arity.
    /// </summary>
    private static bool DeclaresCaching(ImmutableArray<AttributeData> attributes) {
        foreach (var attribute in attributes) {
            var attributeClass = attribute.AttributeClass;

            if (attributeClass?.Name == CacheAttributeName &&
                attributeClass.ContainingNamespace?.ToDisplayString() == CacheAttributeNamespace) {
                return true;
            }
        }

        return false;
    }

    private static bool IsRoute(ImmutableArray<AttributeData> attributes) {
        foreach (var attribute in attributes) {
            var attributeClass = attribute.AttributeClass;

            if (attributeClass != null &&
                Array.IndexOf(VerbAttributeNames, attributeClass.Name) >= 0 &&
                attributeClass.ContainingNamespace?.ToDisplayString() == VerbAttributeNamespace) {
                return true;
            }
        }

        return false;
    }
}
