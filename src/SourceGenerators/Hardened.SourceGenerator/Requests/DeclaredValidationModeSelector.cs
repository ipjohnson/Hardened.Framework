using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// The validation mode a handler's own declarations state, for the published document.
/// </summary>
/// <remarks>
/// <para>
/// The operation, then its class, which is the order the runtime reads them in. The module's
/// declaration is folded in by the document writer, which has the entry point in hand.
/// </para>
/// <para>
/// Read as the constant the argument evaluates to rather than as its spelling, so a cast or a
/// constant names the same member. <c>ValidationStopMode.CollectAll</c> is 0 and
/// <c>StopOnFirstError</c> is 1.
/// </para>
/// </remarks>
public static class DeclaredValidationModeSelector
{
    private const string Attribute = "ValidationModeAttribute";

    private const string AttributeNamespace = "Hardened.Requests.Runtime.Validation";

    /// <summary>
    /// The <c>ValidationStopMode</c> member this operation's nearest declaration names, or null.
    /// </summary>
    public static string? Read(GeneratorSyntaxContext context, MethodDeclarationSyntax method)
    {
        foreach (var attribute in Syntax(method))
        {
            var declaration = context.SemanticModel.GetSymbolInfo(attribute).Symbol?.ContainingType;

            if (
                declaration == null
                || declaration.Name != Attribute
                || declaration.ContainingNamespace?.ToDisplayString() != AttributeNamespace
            )
            {
                continue;
            }

            // Nearest wins, so a declaration whose argument this cannot read still consumes the
            // rung rather than falling through to one further away.
            var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();

            return
                argument != null
                && context.SemanticModel.GetConstantValue(argument.Expression)
                    is { HasValue: true, Value: int value }
                ? Named(value)
                : null;
        }

        return null;
    }

    private static string? Named(int value) =>
        value switch
        {
            0 => ValidationModeNames.CollectAll,
            1 => ValidationModeNames.StopOnFirstError,
            _ => null,
        };

    /// <summary>The method's attributes, then its class's.</summary>
    private static IEnumerable<AttributeSyntax> Syntax(MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                yield return attribute;
            }
        }

        if (method.Parent is not TypeDeclarationSyntax declaringType)
        {
            yield break;
        }

        foreach (var list in declaringType.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                yield return attribute;
            }
        }
    }
}
