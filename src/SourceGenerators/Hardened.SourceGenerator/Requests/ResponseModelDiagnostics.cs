using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// What <c>[ResponseModel(...)]</c> reports for a mode this generator cannot emit yet.
/// </summary>
/// <remarks>
/// <para>
/// An error rather than a warning, and reported for both non-default modes. A mode that is accepted
/// and then does nothing produces an application that compiles, runs, and answers every request the
/// way standard mode does - while its author believes it has a declared response set. That is the
/// silently-wrong outcome the whole design is arranged to avoid, and it is worse than a build
/// failure because nothing about it looks like a failure.
/// </para>
/// <para>
/// Both diagnostics are deleted by the work that implements the mode they name, so their lifetime
/// is short by construction.
/// </para>
/// </remarks>
public static class ResponseModelDiagnostics {

    /// <summary>The <c>Response</c> mode, before the struct and its emit exist.</summary>
    public const string ResponseNotImplementedId = "HRDRM001";

    /// <summary>The <c>Union</c> mode, before the language union is emitted.</summary>
    public const string UnionNotImplementedId = "HRDRM002";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>OpenApiVersionDiagnostics</c> gives: RS2008 looks for the field, and these projects set
    /// <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    internal static DiagnosticDescriptor ResponseNotImplementedDescriptor() => new(
        id: ResponseNotImplementedId,
        title: "Response mode is not implemented yet",
        messageFormat:
            "'{0}' declares [ResponseModel(ResponseModel.Response)], which this version cannot " +
            "emit - handlers would be generated as though the module were Standard, with the " +
            "declared response set silently discarded. Use ResponseModel.Standard and throw for " +
            "the other statuses until Response mode ships.",
        category: "Hardened.Responses",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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
        var descriptor = model switch {
            ResponseModelValue.Response => ResponseNotImplementedDescriptor(),
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
}
