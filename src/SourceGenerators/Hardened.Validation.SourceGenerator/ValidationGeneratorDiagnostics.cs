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
    /// A required member of a value type that nothing can find missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[Required]</c> compiles to a null check, and a value type is never null. So
    /// <c>[Required] public int Category { get; set; }</c> deserializes an omitted member to
    /// <c>0</c>, the check passes because <c>0</c> is not null, and the API answers 201 carrying a
    /// value the caller never sent. An omitted enum is worse: it becomes whichever member was
    /// declared first, so the record reads as a deliberate choice.
    /// </para>
    /// <para>
    /// <b>Why this is a diagnostic and not a fix.</b> Spec-emitted models get <c>[JsonRequired]</c>
    /// written onto them by <c>SchemaEmitter</c>, which is why the same defect was closed there and
    /// not here. A generator cannot add an attribute to a member somebody else wrote, so the only
    /// thing available for a hand-written model is to say so.
    /// </para>
    /// <para>
    /// Both remedies it names are one word. <c>[JsonRequired]</c> is read by the reflection-based
    /// deserializer and the <c>required</c> modifier is read by both it and the source-generated
    /// resolver, which is why the modifier is named first.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor RequiredValueTypeCannotBeMissedDescriptor = new(
        "HRDV003",
        "A required member of a value type cannot be found missing",
        "'{0}.{1}' is required and is '{2}', which is a value type - so an omitted member " +
        "deserializes to default({2}) and the required check passes, because default({2}) is not " +
        "null. Declare it 'required', or add [JsonRequired], so the deserializer rejects the " +
        "absence. Making it '{2}?' also works and changes the model's shape.",
        "Hardened.Validation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports <see cref="RequiredValueTypeCannotBeMissedDescriptor"/> against the member.
    /// </summary>
    /// <remarks>
    /// Located on the member's own declaration rather than the type's, because the fix is one word
    /// on one property and a diagnostic pointing at the class makes the reader find which.
    /// </remarks>
    public static Diagnostic RequiredValueTypeCannotBeMissed(
        INamedTypeSymbol owner, IPropertySymbol property) =>
        Diagnostic.Create(
            RequiredValueTypeCannotBeMissedDescriptor,
            property.Locations.Length > 0 ? property.Locations[0] : Location.None,
            owner.Name,
            property.Name,
            property.Type.ToDisplayString());

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
