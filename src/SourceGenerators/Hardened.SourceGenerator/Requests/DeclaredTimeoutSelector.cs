using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// The deadline a handler's declarations bound it by, for the published document.
/// </summary>
/// <remarks>
/// <para>
/// The same three rungs the runtime resolves, minus the one it cannot see: the operation, its
/// class, then its assembly. The entry point's default is a container registration rather than
/// anything on the operation, and a host-wide knob is not part of the contract an operation
/// publishes, so it is deliberately absent from the document.
/// </para>
/// <para>
/// Keyed on <c>IDeclaresTimeout</c> rather than on <c>[Timeout]</c> by name, for the reason the
/// runtime is: an application's own vocabulary for a deadline is read the same way. What it cannot
/// read is a budget such a declaration computes rather than states, which is why a declaration
/// writing no <c>Milliseconds</c> publishes nothing rather than a guess.
/// </para>
/// </remarks>
public static class DeclaredTimeoutSelector {
    private const string DeclaresTimeout = "IDeclaresTimeout";

    private const string TimeoutNamespace = "Hardened.Requests.Abstract.Timeouts";

    /// <summary>
    /// The budget, status and retry-after this operation declares, or null.
    /// </summary>
    public static (int Milliseconds, int Status, int RetryAfterSeconds)? Read(
        GeneratorSyntaxContext context, MethodDeclarationSyntax method) {
        foreach (var attribute in Syntax(method)) {
            var declaration = context.SemanticModel.GetSymbolInfo(attribute).Symbol?.ContainingType;

            if (declaration == null || !Declares(declaration)) {
                continue;
            }

            var milliseconds = Written(context, attribute, "Milliseconds");

            // Nearest wins, so a declaration this cannot read a budget out of still consumes the
            // rung rather than falling through to one further away and publishing a number the
            // operation does not actually run under.
            return milliseconds == null
                ? null
                : (milliseconds.Value,
                    Written(context, attribute, "Status") ?? 504,
                    Written(context, attribute, "RetryAfterSeconds") ?? 0);
        }

        foreach (var declaration in context.SemanticModel.Compilation.Assembly.GetAttributes()) {
            if (declaration.AttributeClass == null || !Declares(declaration.AttributeClass)) {
                continue;
            }

            var milliseconds = Bound(declaration, "Milliseconds");

            return milliseconds == null
                ? null
                : (milliseconds.Value,
                    Bound(declaration, "Status") ?? 504,
                    Bound(declaration, "RetryAfterSeconds") ?? 0);
        }

        return null;
    }

    /// <summary>The method's attributes, then its class's.</summary>
    private static IEnumerable<AttributeSyntax> Syntax(MethodDeclarationSyntax method) {
        foreach (var list in method.AttributeLists) {
            foreach (var attribute in list.Attributes) {
                yield return attribute;
            }
        }

        if (method.Parent is not TypeDeclarationSyntax declaringType) {
            yield break;
        }

        foreach (var list in declaringType.AttributeLists) {
            foreach (var attribute in list.Attributes) {
                yield return attribute;
            }
        }
    }

    private static bool Declares(INamedTypeSymbol declaration) {
        foreach (var contract in declaration.AllInterfaces) {
            if (contract.Name == DeclaresTimeout &&
                contract.ContainingNamespace?.ToDisplayString() == TimeoutNamespace) {
                return true;
            }
        }

        return false;
    }

    private static int? Written(
        GeneratorSyntaxContext context, AttributeSyntax attribute, string property) {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault(
            candidate => candidate.NameEquals?.Name.Identifier.Text == property);

        if (argument == null) {
            return null;
        }

        return context.SemanticModel.GetConstantValue(argument.Expression) is
            { HasValue: true, Value: int value }
            ? value
            : null;
    }

    private static int? Bound(AttributeData declaration, string property) {
        foreach (var argument in declaration.NamedArguments) {
            if (argument.Key == property && argument.Value.Value is int value) {
                return value;
            }
        }

        return null;
    }
}
