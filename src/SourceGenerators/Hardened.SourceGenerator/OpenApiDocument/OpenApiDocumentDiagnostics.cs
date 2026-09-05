using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// What <c>[Enable&lt;OpenApiDocumentPublishing&gt;]</c> reports when it cannot describe anything,
/// and what a handler's <c>[Operation]</c> reports when two of them name one operation.
/// </summary>
public static class OpenApiDocumentDiagnostics {

    /// <summary>The marker is on a module that declares no routes.</summary>
    public const string EmptyDocumentId = "HRDOA003";

    /// <summary>Two handlers declare the same <c>[Operation]</c> id.</summary>
    public const string DuplicateOperationIdId = "HRDOA004";

    internal static DiagnosticDescriptor DuplicateOperationIdDescriptor() => new(
        id: DuplicateOperationIdId,
        title: "Two handlers declare the same operation id",
        messageFormat:
        "[Operation(\"{0}\")] is declared on {1}. An operationId names one operation in the " +
        "document, so a client generated from it would have two methods with one name. Give " +
        "each handler its own id.",
        category: "Hardened.OpenApi",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports every id that more than one handler declared.
    /// </summary>
    /// <remarks>
    /// Reported whether or not the module publishes a document: the id is a declaration on the
    /// handler, and an exported document reads it the same way a served one does.
    /// </remarks>
    public static void ReportDuplicateOperationIds(
        SourceProductionContext context, IReadOnlyList<RequestHandlerModel> handlers) {
        var byId = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var handler in handlers) {
            if (handler.OperationId == null) {
                continue;
            }

            if (!byId.TryGetValue(handler.OperationId, out var declaredBy)) {
                declaredBy = new List<string>();
                byId[handler.OperationId] = declaredBy;
            }

            declaredBy.Add(handler.ControllerType.Name + "." + handler.HandlerMethod);
        }

        foreach (var pair in byId) {
            if (pair.Value.Count > 1) {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DuplicateOperationIdDescriptor(),
                        Location.None,
                        pair.Key,
                        string.Join(" and ", pair.Value)));
            }
        }
    }

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
