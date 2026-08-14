using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Validation;

public static class HandlerValidationDiagnostics {

    /// <summary>
    /// A constraint attribute written directly on a handler's parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is a warning and not a feature.</b> Reading a constraint off a parameter needs
    /// the same work reading one off a property needs, and ValidationModules already does all of
    /// it: the two attribute vocabularies, the pattern reference form, the AOT pattern policy, and
    /// the diagnostics for a constraint that does not fit its member's type. All of that is reached
    /// through a front-end typed on <c>IPropertySymbol</c>, and its pattern handling is private. A
    /// parameter is not a property, so compiling these here would mean a second implementation of a
    /// policy that ValidationModules owns - which is exactly the arrangement the OpenAPI path was
    /// rebuilt to avoid.
    /// </para>
    /// <para>
    /// Warning rather than silence because the alternative is the failure this whole design
    /// refuses: a constraint that was declared, was never compiled, and never says so. Put the
    /// constraint on the model the parameter binds to and it is compiled today.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor ConstraintOnParameter = new(
        "HRDV001",
        "Constraint on a handler parameter is not compiled",
        "'{0}' on parameter '{1}' is not compiled into a validator. Constraints are read from the " +
        "types parameters bind to, not from the parameters themselves - move it onto a property of " +
        "a model type, or declare the operation in an OpenAPI specification, where the build task " +
        "compiles parameter constraints.",
        "Hardened.Validation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
