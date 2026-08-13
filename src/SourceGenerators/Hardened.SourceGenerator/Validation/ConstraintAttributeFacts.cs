using Microsoft.CodeAnalysis;
using ValidationModules.SourceGenerator.Impl;

namespace Hardened.SourceGenerator.Validation;

/// <summary>
/// Recognises the two constraint vocabularies, so a handler generator can tell a constraint from a
/// binding attribute.
/// </summary>
/// <remarks>
/// Needed because a handler's parameter attributes are open-ended: anything the generator does not
/// recognise is treated as a custom binder and emitted as one. A constraint attribute landing in
/// that branch does not merely fail to validate - it takes over how the parameter is bound.
/// </remarks>
public static class ConstraintAttributeFacts {

    public static bool IsConstraint(AttributeData attribute) =>
        IsConstraintNamespace(attribute.AttributeClass?.ContainingNamespace?.ToDisplayString());

    /// <summary>
    /// Whether an attribute in source is a constraint, resolved through the semantic model.
    /// </summary>
    /// <remarks>
    /// By symbol rather than by name. <c>[Required]</c> is declared in both vocabularies and could
    /// equally be someone's own attribute, and treating a name as proof would take a consumer's
    /// unrelated attribute out of the binding path it was written for.
    /// </remarks>
    public static bool IsConstraint(GeneratorSyntaxContext context, SyntaxNode attribute) {
        var symbol = context.SemanticModel.GetSymbolInfo(attribute).Symbol;

        var containing = symbol?.ContainingType?.ContainingNamespace?.ToDisplayString();

        return IsConstraintNamespace(containing);
    }

    private static bool IsConstraintNamespace(string? containingNamespace) =>
        containingNamespace is KnownTypes.ConstraintsNamespace or KnownTypes.DataAnnotationsNamespace;
}
