using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ValidationModules.SourceGenerator.Impl;

namespace Hardened.SourceGenerator.Validation;

/// <summary>
/// The types a ValidationModules rules class describes.
/// </summary>
/// <remarks>
/// ValidationModules' generator builds a validator for every type an <c>IValidationRulesFor&lt;T&gt;</c>
/// names, whether or not the type carries a constraint attribute. A handler has to know that to
/// call the validator, and the attributes on the type cannot tell it.
/// </remarks>
public static class RulesTargets
{
    /// <summary>
    /// Every type the declaration's rules classes describe. Empty for a type that is not a rules
    /// class, which is almost every type.
    /// </summary>
    public static ImmutableArray<string> Of(GeneratorSyntaxContext context, CancellationToken token)
    {
        if (
            context.SemanticModel.GetDeclaredSymbol(context.Node, token)
            is not INamedTypeSymbol type
        )
        {
            return ImmutableArray<string>.Empty;
        }

        ImmutableArray<string>.Builder? targets = null;

        // AllInterfaces, because one class may describe several types and a rules class may
        // inherit the contract.
        foreach (var contract in type.AllInterfaces)
        {
            if (
                contract.ConstructedFrom.ToDisplayString() == KnownTypes.ValidationRulesForInterface
                && contract.TypeArguments.Length == 1
                && contract.TypeArguments[0] is INamedTypeSymbol target
            )
            {
                (targets ??= ImmutableArray.CreateBuilder<string>()).Add(Key(target));
            }
        }

        return targets?.ToImmutable() ?? ImmutableArray<string>.Empty;
    }

    /// <summary>How a type is named in the set, so a lookup and a declaration agree.</summary>
    public static string Key(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
