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
