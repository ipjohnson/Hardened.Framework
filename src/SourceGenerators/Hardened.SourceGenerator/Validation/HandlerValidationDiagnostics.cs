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
    /// <summary>
    /// Constraints declared in an assembly nothing compiles validators for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The constraint attributes are ordinary types from a package the application already
    /// references; compiling them into a validator is a second package, an analyzer, and an
    /// application that references the first without the second builds clean and enforces nothing.
    /// Code-first is the silent form - the constraints simply never run. Spec-first is the loud one
    /// - the filter is attached against a validator nobody emitted, and every constrained operation
    /// answers a 500 naming three hypotheses.
    /// </para>
    /// <para>
    /// A warning rather than an error: the constraints still describe the contract, the document
    /// still publishes them, and an assembly that declares models for someone else to validate is a
    /// real arrangement. What it must not be is a surprise.
    /// </para>
    /// <para>
    /// Built per call rather than held in a static field, unlike its neighbour: RS2008 looks for
    /// the field, and the neighbour predates the rule being enforced here.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor NoValidationGenerator() => new(
        "HRDV006",
        "Constraints are declared and nothing compiles them",
        "'{0}' declares constraints and nothing in this project compiles them into a validator, " +
        "so none of them is enforced. Reference Hardened.Validation.SourceGenerator as an " +
        "analyzer, or remove the constraint attributes if this assembly is not meant to enforce " +
        "them.",
        "Hardened.Validation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

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
