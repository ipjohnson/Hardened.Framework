using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// What <c>[Enable&lt;OpenApiDocumentPublishing&gt;]</c> reports when it cannot describe anything.
/// </summary>
public static class OpenApiDocumentDiagnostics {

    /// <summary>The marker is on a module that declares no routes.</summary>
    public const string EmptyDocumentId = "HRDOA003";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>OpenApiVersionDiagnostics</c> gives: RS2008 looks for the field, and these projects set
    /// <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    internal static DiagnosticDescriptor EmptyDocumentDescriptor() => new(
        id: EmptyDocumentId,
        title: "The published OpenAPI document describes no operations",
        messageFormat:
        "'{0}' enables OpenApiDocumentPublishing and declares no routes, so the document served " +
        "at {1} is \"paths\": {{}}. The document is written from the routes in the same " +
        "compilation as the attribute - move [Enable<OpenApiDocumentPublishing>] to the module " +
        "that declares them. With it on both, the empty one shadows the real one.",
        category: "Hardened.OpenApi",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports a marker that will publish an empty document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect this exists for shipped in the template: the attribute sat on the host module,
    /// which composes the library rather than declaring routes itself, so every code-first
    /// application generated from it served an empty document. Nothing failed. The build was clean,
    /// <c>/openapi.json</c> answered 200, and the reference page rendered zero operations - which
    /// reads exactly like an API with no routes.
    /// </para>
    /// <para>
    /// A warning rather than an error, because an application whose document is genuinely empty
    /// still runs, and because a module that declares no routes today may declare some tomorrow.
    /// What it must not do is publish emptiness silently.
    /// </para>
    /// </remarks>
    public static void ReportEmptyDocument(
        SourceProductionContext context, string entryPointName, string documentPath) {
        context.ReportDiagnostic(
            Diagnostic.Create(
                EmptyDocumentDescriptor(), Location.None, entryPointName, documentPath));
    }
}
