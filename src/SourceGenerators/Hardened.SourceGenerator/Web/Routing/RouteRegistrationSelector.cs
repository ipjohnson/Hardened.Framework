using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// Whether the compilation declares a type that registers routes at startup.
/// </summary>
/// <remarks>
/// <para>
/// The gate on the handler catalog. An application that registers nothing at run time generates
/// exactly what it generated before the feature existed, which keeps the checked-in routing
/// fixtures and every existing application unchanged.
/// </para>
/// <para>
/// <b>Syntactic, deliberately.</b> Reading the base type through the semantic model would mean
/// binding every type declaration in the compilation to answer a question whose only consequence is
/// whether one extra class is emitted. The cost of being wrong is an emitted catalog nothing
/// resolves, which compiles and is dead; the cost of binding everything is paid on every keystroke.
/// </para>
/// </remarks>
public static class RouteRegistrationSelector
{
    private const string InterfaceName = "IRouteRegistration";

    public static bool Predicate(SyntaxNode node, CancellationToken cancellationToken) =>
        node is TypeDeclarationSyntax { BaseList: not null };

    public static bool Transform(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        var declaration = (TypeDeclarationSyntax)context.Node;

        foreach (var baseType in declaration.BaseList!.Types)
        {
            if (Names(baseType.Type))
            {
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// The name as written, qualified or not. <c>Hardened.Web.Runtime.Routing.IRouteRegistration</c>
    /// and a plain <c>IRouteRegistration</c> under a using both reach here.
    /// </remarks>
    private static bool Names(TypeSyntax type) =>
        type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text == InterfaceName,
            QualifiedNameSyntax qualified => Names(qualified.Right),
            AliasQualifiedNameSyntax aliased => Names(aliased.Name),
            _ => false,
        };
}
