using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// Reads <c>[Template&lt;T&gt;]</c> off a handler.
/// </summary>
/// <remarks>
/// A generic attribute cannot be found with <c>SyntaxNodeExtensions.GetAttribute</c>, which
/// compares the whole name text - <c>[Template&lt;Views.Fortunes&gt;]</c> spells its name as
/// <c>Template&lt;Views.Fortunes&gt;</c> and matches nothing. The type argument is what is wanted
/// anyway, so this walks the attribute lists itself.
/// </remarks>
public static class TemplateAttributeSelector {
    private const string AttributeName = "Template";

    private const string AttributeSuffix = "Attribute";

    /// <summary>
    /// The view type, or null when the handler declares none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The semantic model usually will not resolve it, and that is expected rather than a failure:
    /// a view is another generator's output, and generators all see the original compilation rather
    /// than each other's. So the name is taken as written when the symbol is missing, with an empty
    /// namespace, which is exactly how the attribute itself is already re-emitted into the handler's
    /// metadata array.
    /// </para>
    /// <para>
    /// The consequence is that the name has to be resolvable from the handler's own namespace -
    /// <c>[Template&lt;Views.Fortunes&gt;]</c> or a fully qualified name, not a bare name reached
    /// through a <c>using</c> in the declaring file, since generated code carries none of those.
    /// When it is not, the generated code fails to compile with a <c>CS0246</c> naming the type,
    /// which is a legible failure rather than a silent one.
    /// </para>
    /// </remarks>
    public static ITypeDefinition? Read(
        GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration) {
        foreach (var attributeList in methodDeclaration.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                var argument = TypeArgument(attribute);

                if (argument != null) {
                    return argument.GetTypeDefinition(context) ??
                           TypeDefinition.Get("", argument.ToString().Trim());
                }
            }
        }

        return null;
    }

    private static TypeSyntax? TypeArgument(AttributeSyntax attribute) {
        var generic = attribute.Name as GenericNameSyntax ??
                      (attribute.Name as QualifiedNameSyntax)?.Right as GenericNameSyntax ??
                      (attribute.Name as AliasQualifiedNameSyntax)?.Name as GenericNameSyntax;

        if (generic == null || generic.TypeArgumentList.Arguments.Count != 1) {
            return null;
        }

        var name = generic.Identifier.Text;

        return name == AttributeName || name == AttributeName + AttributeSuffix
            ? generic.TypeArgumentList.Arguments[0]
            : null;
    }
}
