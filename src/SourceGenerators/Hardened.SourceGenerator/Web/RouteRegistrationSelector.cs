using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// The types in this compilation that register routes at startup.
/// </summary>
/// <remarks>
/// <para>
/// Two things read this. It gates the handler catalog, so an application that registers nothing at
/// run time generates exactly what it generated before the feature existed. And it registers each
/// implementer in the container, so <c>IRouteRegistration</c> is a declaration rather than a
/// declaration plus a registration somebody has to remember - which is also what lets the entry
/// point implement it, since nothing puts an entry point in the container.
/// </para>
/// <para>
/// <b>Syntactic predicate, semantic transform.</b> The predicate runs on every type declaration in
/// the compilation and only looks at the names written in the base list, which is a string compare.
/// The transform binds, and only for the handful of declarations that named the interface - so an
/// application with one registration binds one type rather than every type that has a base list at
/// all.
/// </para>
/// </remarks>
public static class RouteRegistrationSelector
{
    private const string InterfaceName = "IRouteRegistration";

    private const string InterfaceNamespace = "Hardened.Web.Runtime.Routing";

    public static bool Predicate(SyntaxNode node, CancellationToken cancellationToken) =>
        node is TypeDeclarationSyntax { BaseList: not null } declaration
        && Names(declaration.BaseList);

    /// <summary>
    /// The fully qualified name of the type, or null where it does not implement the interface or
    /// cannot be constructed.
    /// </summary>
    public static string? Transform(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        if (
            context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken)
            is not INamedTypeSymbol symbol
        )
        {
            return null;
        }

        // Abstract and static types are excluded rather than reported. An abstract base declaring
        // the interface for its subclasses to implement is an ordinary thing to write, and
        // registering it would be a container failure at startup about a type the author never
        // meant to register.
        if (symbol.IsAbstract || symbol.IsStatic)
        {
            return null;
        }

        foreach (var implemented in symbol.AllInterfaces)
        {
            if (
                implemented.Name == InterfaceName
                && implemented.ContainingNamespace?.ToDisplayString() == InterfaceNamespace
            )
            {
                return symbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(
                        SymbolDisplayGlobalNamespaceStyle.Omitted
                    )
                );
            }
        }

        return null;
    }

    /// <remarks>
    /// The name as written, qualified or not. <c>Hardened.Web.Runtime.Routing.IRouteRegistration</c>
    /// and a plain <c>IRouteRegistration</c> under a using both reach the transform, which is what
    /// decides.
    /// </remarks>
    private static bool Names(BaseListSyntax baseList)
    {
        foreach (var baseType in baseList.Types)
        {
            if (Names(baseType.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Names(TypeSyntax type) =>
        type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text == InterfaceName,
            QualifiedNameSyntax qualified => Names(qualified.Right),
            AliasQualifiedNameSyntax aliased => Names(aliased.Name),
            _ => false,
        };
}
