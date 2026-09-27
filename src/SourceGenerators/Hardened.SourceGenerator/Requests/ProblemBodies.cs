using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// Whether a declared body is a problem document, which the document publishes as
/// <c>application/problem+json</c>.
/// </summary>
/// <remarks>
/// By the interface the runtime reads, so the document and the JSON serializers agree about the
/// same type: <c>ProblemJson.ContentTypeFor</c> labels a failure whose body implements it.
/// </remarks>
internal static class ProblemBodies
{
    private const string ProblemInterface = "Hardened.Requests.Abstract.Responses.IProblemDetails";

    /// <summary>
    /// The type's shared <c>Default</c> instance as a qualified expression, for a failure status, or
    /// null.
    /// </summary>
    /// <remarks>
    /// Every problem record Hardened ships but <c>RateLimited</c> carries one, holding a general
    /// <c>detail</c> and nothing about a request. An application's own type qualifies on the same
    /// terms: a public static field named <c>Default</c> of the type itself.
    /// </remarks>
    public static string? DefaultInstance(ITypeSymbol? type, int status)
    {
        if (status < 400 || type is not INamedTypeSymbol named)
        {
            return null;
        }

        foreach (var member in named.GetMembers("Default"))
        {
            if (
                member
                    is IFieldSymbol
                    {
                        IsStatic: true,
                        DeclaredAccessibility: Accessibility.Public
                    } field
                && SymbolEqualityComparer.Default.Equals(field.Type, named)
            )
            {
                return named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".Default";
            }
        }

        return null;
    }

    public static bool Implement(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (contract.ToDisplayString() == ProblemInterface)
            {
                return true;
            }
        }

        return false;
    }
}
