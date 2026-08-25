using System;
using Microsoft.CodeAnalysis;
using ValidationModules.SourceGenerator.Impl.Models;

namespace Hardened.Validation.SourceGenerator;

/// <summary>
/// What this generator says when it cannot emit what it was asked for.
/// </summary>
public static class ValidationGeneratorDiagnostics {

    /// <summary>
    /// Two validators claimed the same generated file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An error rather than a warning, and reported rather than thrown.</b> A generator that
    /// throws is reported by Roslyn as <c>CS8785</c>, which is a warning: the generator dies part
    /// way through, every validator it had not yet emitted is lost, and the build succeeds. The
    /// application then starts and answers 500 on the first request that validates anything,
    /// blaming a registration that was never written.
    /// </para>
    /// <para>
    /// The shape that produced this - one partial type reaching the generator once per declaring
    /// file - is fixed upstream, so this is a backstop rather than a routine message. It exists
    /// because the failure mode it replaces is invisible, and a backstop that says nothing is worth
    /// nothing.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DuplicateValidatorSourceDescriptor = new(
        "HRDV002",
        "Two validators claimed the same generated file",
        "A validator for '{0}' could not be added: {1} No validator is generated for it, and a " +
        "handler that validates it will fail at runtime. This is a defect in " +
        "Hardened.Validation.SourceGenerator rather than in the application - please report it.",
        "Hardened.Validation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports <see cref="DuplicateValidatorSourceDescriptor"/> against the type that collided.
    /// </summary>
    /// <remarks>
    /// Located on the model's own declaration where there is one. A diagnostic with no location is
    /// attributed to the project rather than to a file, which is the right answer for a fault in the
    /// generator and the wrong one for the reader trying to find the type it names.
    /// </remarks>
    public static Diagnostic DuplicateValidatorSource(
        ValidatedTypeModel model, ArgumentException exception) =>
        Diagnostic.Create(
            DuplicateValidatorSourceDescriptor,
            Location.None,
            model.Namespace.Length == 0 ? model.ValidatorName : model.Namespace + "." + model.ValidatorName,
            exception.Message);
}
