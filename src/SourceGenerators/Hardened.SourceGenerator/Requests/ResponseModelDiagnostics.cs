using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// What a declared response set is rejected for.
/// </summary>
/// <remarks>
/// <para>
/// <c>HRDRM001</c> and <c>HRDRM002</c> were the two modes this generator could not emit, and both
/// are gone: <c>Response</c> when <c>UnionResponseSelector</c> and <c>InvokeMethodCodeGenerator</c>
/// landed, and <c>Union</c> when CSharpAuthor gained the keyword. Neither id is reused, because a
/// consumer who suppressed one should not silently acquire a suppression for something else.
/// </para>
/// <para>
/// What is left describes a case set rather than a mode. Both of these are ambiguities that land in
/// the shipped contract rather than in the generated code - the switch compiles and runs either way,
/// which is exactly why nothing else would surface them.
/// </para>
/// </remarks>
public static class ResponseModelDiagnostics {

    /// <summary>A case type that everything is assignable to.</summary>
    public const string UntypedCaseId = "HRDRM003";

    /// <summary>Two cases of one set where one is assignable to the other.</summary>
    public const string AssignableCasesId = "HRDRM004";

    /// <summary>
    /// <c>object</c> or <c>dynamic</c> as a case.
    /// </summary>
    internal static DiagnosticDescriptor UntypedCaseDescriptor() => new(
        id: UntypedCaseId,
        title: "A response case must be a specific type",
        messageFormat:
            "'{0}' declares '{1}' as a response case. Everything is assignable to it, so the " +
            "generated dispatch would answer that case's status for every response the handler " +
            "returns, and the document would describe every one of them with its schema.",
        category: "Hardened.Responses",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// One case assignable to another, at different statuses.
    /// </summary>
    /// <remarks>
    /// An error rather than a sort order, and only across statuses. Two cases of one status are
    /// going into a single <c>oneOf</c> anyway and their order does not decide anything - but
    /// mapping them to different statuses puts the ambiguity in the shipped document, where the
    /// subtype's schema is a superset of the base's and a payload validates against both. No
    /// arrangement of switch arms fixes an artifact that is already ambiguous.
    /// </remarks>
    internal static DiagnosticDescriptor AssignableCasesDescriptor() => new(
        id: AssignableCasesId,
        title: "Response cases at different statuses must not be assignable to one another",
        messageFormat:
            "'{0}' declares '{1}' at {2} and '{3}' at {4}, and one is assignable to the other. The " +
            "document then describes two statuses with schemas where one is a superset of the " +
            "other, so a payload validates against both and a reader cannot tell which status it " +
            "belongs to. Give them a common base neither case is, or map them to one status.",
        category: "Hardened.Responses",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports whatever the selector found wrong with a handler's declared case set.
    /// </summary>
    /// <remarks>
    /// The finding is decided where the symbols are and reported here, so the message names types
    /// the transform could see and this method needs no compilation of its own.
    /// </remarks>
    public static void ReportCaseSetFindings(
        SourceProductionContext context, string handler, string? finding) {
        if (string.IsNullOrEmpty(finding)) {
            return;
        }

        var fields = UnionResponseSelector.DecodeFinding(finding!);

        if (fields.Count == 2 && fields[0] == UnionResponseSelector.UntypedFinding) {
            context.ReportDiagnostic(Diagnostic.Create(
                UntypedCaseDescriptor(), Location.None, handler, fields[1]));

            return;
        }

        if (fields.Count == 5 && fields[0] == UnionResponseSelector.AssignableFinding) {
            context.ReportDiagnostic(Diagnostic.Create(
                AssignableCasesDescriptor(), Location.None,
                handler, fields[1], fields[2], fields[3], fields[4]));
        }
    }
}
