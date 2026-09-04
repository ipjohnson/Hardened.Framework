using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Validation;

public static class HandlerValidationDiagnostics {

    /// <summary>
    /// A <c>When</c> or <c>Unless</c> on a constraint written on a handler's parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two name a member of the model the constraint sits on - a bool property or method the
    /// generated validator calls before checking - and a handler parameter sits on no model. The
    /// front end that reads the constraint would look the member up on a type it was never given,
    /// so the one decision about a parameter's constraint this generator makes itself is to refuse
    /// this shape before the reader sees it.
    /// </para>
    /// <para>
    /// An error, because a condition that is ignored is a constraint that runs when its author said
    /// it should not. <c>HRDV001</c>, which warned that a constraint on a parameter was not compiled
    /// at all, is retired: it is compiled now.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor ConditionOnParameterConstraint = new(
        "HRDV005",
        "A condition on a parameter constraint names a model member",
        "'{0}' on [{1}] for parameter '{2}' names a member of the model the constraint sits on, and " +
        "a handler parameter sits on no model. Remove the condition, or move the constraint onto a " +
        "property of a model type where the member it names is declared.",
        "Hardened.Validation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
