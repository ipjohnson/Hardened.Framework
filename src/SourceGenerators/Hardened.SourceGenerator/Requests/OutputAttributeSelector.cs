using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// Reads <c>[Output&lt;T&gt;]</c> off a handler.
/// </summary>
/// <remarks>
/// A generic attribute cannot be found with <c>SyntaxNodeExtensions.GetAttribute</c>, which
/// compares the whole name text - <c>[Output&lt;Views.Fortunes&gt;]</c> spells its name as
/// <c>Output&lt;Views.Fortunes&gt;</c> and matches nothing. The type argument is what is wanted
/// anyway, so this walks the attribute lists itself.
/// </remarks>
public static class OutputAttributeSelector
{
    private const string AttributeName = "Output";

    private const string AttributeSuffix = "Attribute";

    /// <summary>
    /// The output type, or null when the handler declares none.
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
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration
    ) => Read(context, methodDeclaration.AttributeLists);

    /// <summary>
    /// The same, off whatever carries the attribute lists.
    /// </summary>
    /// <remarks>
    /// C# has allowed attributes on a lambda since version 10, and a route registered with one is a
    /// handler like any other - so the attribute is read off the lambda's own lists here rather
    /// than off a method declaration it does not have.
    /// </remarks>
    public static ITypeDefinition? Read(
        GeneratorSyntaxContext context,
        SyntaxList<AttributeListSyntax> attributeLists
    )
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var argument = TypeArgument(attribute);

                if (argument != null)
                {
                    // A TypeParameterDefinition, which is "written as itself in every output
                    // mode" - not because the name is a type parameter, but because that is the
                    // contract described above: the text resolves in the generated file's own
                    // namespace, walking outward the way C# does. An empty-namespace
                    // TypeDefinition stopped meaning that in CSharpAuthor 2.0, which qualifies it
                    // to global:: - and prefixing the handler's namespace instead would not walk.
                    return argument.GetTypeDefinition(context)
                        ?? new TypeParameterDefinition(argument.ToString().Trim());
                }
            }
        }

        return null;
    }

    private static TypeSyntax? TypeArgument(AttributeSyntax attribute)
    {
        var generic =
            attribute.Name as GenericNameSyntax
            ?? (attribute.Name as QualifiedNameSyntax)?.Right as GenericNameSyntax
            ?? (attribute.Name as AliasQualifiedNameSyntax)?.Name as GenericNameSyntax;

        if (generic == null || generic.TypeArgumentList.Arguments.Count != 1)
        {
            return null;
        }

        var name = generic.Identifier.Text;

        return name == AttributeName || name == AttributeName + AttributeSuffix
            ? generic.TypeArgumentList.Arguments[0]
            : null;
    }
}
