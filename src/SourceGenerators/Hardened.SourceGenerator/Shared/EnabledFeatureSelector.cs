using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// Reads the <c>[Enable&lt;T&gt;]</c> attributes on an entry point.
/// </summary>
/// <remarks>
/// <para>
/// This is what replaces feature detection. A generator used to ask the compilation whether a type
/// was present - <c>GetTypeByMetadataName</c> against a marker, or a <c>CompilationProvider</c>
/// gate - which costs incrementality and is wrong whenever the thing being probed for is itself
/// generated. To write <c>[Enable&lt;RazorTemplates&gt;]</c> you must be able to name the
/// marker, so the reference is a compile-time requirement rather than something to discover.
/// </para>
/// <para>
/// It also says <em>which</em> entry point. Generated template bases and link types need the same
/// per-module scoping the routing table already has, and without a signal a generator facing an
/// assembly with two entry points has to guess or collide.
/// </para>
/// </remarks>
public static class EnabledFeatureSelector {
    private const string AttributeName = "Enable";

    private const string AttributeSuffix = "Attribute";

    public static IReadOnlyList<EnabledFeatureModel> Read(
        GeneratorSyntaxContext context, ClassDeclarationSyntax declaration, CancellationToken cancellationToken) {
        List<EnabledFeatureModel>? features = null;

        foreach (var attributeList in declaration.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var marker = Marker(context, attribute);

                if (marker == null) {
                    continue;
                }

                features ??= new List<EnabledFeatureModel>();

                features.Add(new EnabledFeatureModel(
                    marker.GetTypeDefinition(), Facets(marker), IsDependencyModule(marker)));
            }
        }

        return (IReadOnlyList<EnabledFeatureModel>?)features ?? Array.Empty<EnabledFeatureModel>();
    }

    /// <summary>
    /// The type argument of an <c>[Enable&lt;T&gt;]</c>, or null for any other attribute.
    /// </summary>
    /// <remarks>
    /// Both spellings of the name are accepted, and the qualified form as well:
    /// <c>[Enable&lt;X&gt;]</c>, <c>[EnableAttribute&lt;X&gt;]</c> and
    /// <c>[Hardened.Shared.Runtime.Attributes.Enable&lt;X&gt;]</c> are the same attribute, and a
    /// generator that only recognised the first would silently do nothing for a project that wrote
    /// one of the others.
    /// </remarks>
    private static INamedTypeSymbol? Marker(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        var generic = attribute.Name as GenericNameSyntax ??
                      (attribute.Name as QualifiedNameSyntax)?.Right as GenericNameSyntax ??
                      (attribute.Name as AliasQualifiedNameSyntax)?.Name as GenericNameSyntax;

        if (generic == null || generic.TypeArgumentList.Arguments.Count != 1) {
            return null;
        }

        var name = generic.Identifier.Text;

        if (name != AttributeName && name != AttributeName + AttributeSuffix) {
            return null;
        }

        return context.SemanticModel.GetSymbolInfo(generic.TypeArgumentList.Arguments[0]).Symbol
            as INamedTypeSymbol;
    }

    /// <summary>
    /// Whether the marker is also a DependencyModules module.
    /// </summary>
    /// <remarks>
    /// Two tests because there are two situations. A marker from a referenced package is already
    /// compiled, so the interface is in metadata. One declared in this compilation gets that
    /// interface from DependencyModules' own generator, which this generator cannot see - so its
    /// <c>[DependencyModule]</c> attribute is read instead, which is in source either way.
    /// </remarks>
    private static bool IsDependencyModule(INamedTypeSymbol marker) {
        foreach (var contract in marker.AllInterfaces) {
            if (contract.Name == "IDependencyModule" &&
                contract.ContainingNamespace?.ToDisplayString() == "DependencyModules.Runtime.Interfaces") {
                return true;
            }
        }

        foreach (var attribute in marker.GetAttributes()) {
            var name = attribute.AttributeClass?.Name;

            if (name == "DependencyModuleAttribute" || name == "DependencyModule") {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<FeatureFacet> Facets(INamedTypeSymbol marker) {
        var facets = new List<FeatureFacet>();

        foreach (var attribute in marker.GetAttributes()) {
            var attributeClass = attribute.AttributeClass;

            if (attributeClass == null) {
                continue;
            }

            var name = attributeClass.Name;

            if (name.EndsWith(AttributeSuffix, StringComparison.Ordinal)) {
                name = name.Substring(0, name.Length - AttributeSuffix.Length);
            }

            string? value = null;
            ITypeDefinition? typeValue = null;

            if (attribute.ConstructorArguments.Length > 0) {
                var argument = attribute.ConstructorArguments[0];

                if (argument.Kind == TypedConstantKind.Type) {
                    // An unbound generic - typeof(HardenedHtmlTemplate<>) - reduces to its
                    // namespace and name, which is exactly what a generated base needs before it
                    // closes the type over its own parameter.
                    typeValue = (argument.Value as ITypeSymbol)?.GetTypeDefinition();
                }
                else {
                    value = argument.Value as string;
                }
            }

            facets.Add(new FeatureFacet(name, value, typeValue));
        }

        return facets;
    }
}
