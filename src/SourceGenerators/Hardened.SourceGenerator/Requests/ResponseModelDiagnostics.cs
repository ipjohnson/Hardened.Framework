using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// What <c>[ResponseModel(...)]</c> reports for a mode this generator cannot emit yet.
/// </summary>
/// <remarks>
/// <para>
/// An error rather than a warning. A mode that is accepted and then does nothing produces an
/// application that compiles, runs, and answers every request the way standard mode does - while its
/// author believes it has a declared response set. That is the silently-wrong outcome the whole
/// design is arranged to avoid, and it is worse than a build failure because nothing about it looks
/// like a failure.
/// </para>
/// <para>
/// Each diagnostic is deleted by the work that implements the mode it names, so its lifetime is
/// short by construction. <c>HRDRM001</c> was the <c>Response</c> mode and is gone: code-first
/// <c>Response</c> is emitted by <c>UnionResponseSelector</c> and <c>InvokeMethodCodeGenerator</c>,
/// and the id is not reused, because a consumer suppressing it should not silently acquire a
/// suppression for something else.
/// </para>
/// </remarks>
public static class ResponseModelDiagnostics {

    /// <summary>The <c>Union</c> mode, before the language union is emitted.</summary>
    public const string UnionNotImplementedId = "HRDRM002";

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
    /// Built per call rather than held in a static field, for the reason
    /// <c>OpenApiVersionDiagnostics</c> gives: RS2008 looks for the field, and these projects set
    /// <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    internal static DiagnosticDescriptor UnionNotImplementedDescriptor() => new(
        id: UnionNotImplementedId,
        title: "Union mode is not implemented yet",
        messageFormat:
            "'{0}' declares [ResponseModel(ResponseModel.Union)], which this version cannot emit - " +
            "handlers would be generated as though the module were Standard, with the declared " +
            "response set silently discarded. Union mode also requires a C# 15 compiler when it " +
            "ships; ResponseModel.Response is the equivalent for any compiler.",
        category: "Hardened.Responses",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports whichever mode the entry point declared that cannot be emitted.
    /// </summary>
    /// <remarks>
    /// Takes the already-read value rather than the model, so the attribute is read once per entry
    /// point and the caller that needs the mode for its own emit is the one that read it.
    /// </remarks>
    public static void ReportUnimplementedMode(
        SourceProductionContext context, ResponseModelValue model, string entryPointName) {
        // Response is absent deliberately. Code-first, the return type already decides: a handler
        // returning something that matches the basic union pattern gets the dispatch whatever the
        // module declared, so a module in Response mode works. What the mode still governs is the
        // signature the IDL-first emitter writes, and an analyzer checking methods against the
        // declared intent - neither of which is a reason to fail this build.
        var descriptor = model switch {
            ResponseModelValue.Union => UnionNotImplementedDescriptor(),
            _ => null
        };

        if (descriptor == null) {
            return;
        }

        // Location.None: the attribute is on the entry point, and the model carries the type rather
        // than the syntax it was declared with. The entry point is named in the message, which is
        // what a reader needs to find it - an assembly has few enough of them that a name locates
        // it, unlike a handler or a route.
        context.ReportDiagnostic(
            Diagnostic.Create(descriptor, Location.None, entryPointName));
    }

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
