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

    /// <summary>Two types are published under one component name.</summary>
    public const string SchemaNameCollisionId = "HRDOA005";

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

    internal static DiagnosticDescriptor SchemaNameCollisionDescriptor() => new(
        id: SchemaNameCollisionId,
        title: "Two types are published as one schema component",
        messageFormat:
        "Two different types are both published as \"{0}\", reached from {1}. A component name " +
        "identifies one schema, so whichever is written last describes both - and a client " +
        "generated from the document has one of these operations wrong. Rename one of the types.",
        category: "Hardened.OpenApi",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports every component name that two different types were written under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A component is named by the type's own name, unqualified. That is the spelling every client
    /// generator produces and it is worth keeping, but it means two types with one name in
    /// different namespaces - or in different controllers, which is where it actually happens -
    /// collide, and the merge keeps whichever arrived last. The document stays valid and one
    /// operation is described with another type's shape.
    /// </para>
    /// <para>
    /// Compared by the schema each type produced rather than by the symbol, because the models
    /// carry written JSON by the time the document is assembled. Two types that produce identical
    /// JSON are not reported: the document is then correct whichever one wins, and reporting it
    /// would name a defect the reader cannot see.
    /// </para>
    /// <para>
    /// A warning rather than an error. The document is readable and an application shipping this
    /// today still serves; the point is that it stops being silent. This repository builds with
    /// warnings as errors in CI, so it is fatal where it needs to be.
    /// </para>
    /// </remarks>
    public static void ReportSchemaNameCollisions(
        SourceProductionContext context, IReadOnlyList<RequestHandlerModel> handlers) {
        var byName = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.Ordinal);

        foreach (var handler in handlers) {
            var owner = handler.ControllerType.Name + "." + handler.HandlerMethod;

            foreach (var schema in Schemas(handler)) {
                foreach (var component in schema.Components) {
                    if (!byName.TryGetValue(component.Name, out var shapes)) {
                        shapes = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                        byName[component.Name] = shapes;
                    }

                    if (!shapes.TryGetValue(component.Json, out var reachedFrom)) {
                        reachedFrom = new SortedSet<string>(StringComparer.Ordinal);
                        shapes[component.Json] = reachedFrom;
                    }

                    reachedFrom.Add(owner);
                }
            }
        }

        foreach (var pair in byName.OrderBy(entry => entry.Key, StringComparer.Ordinal)) {
            if (pair.Value.Count < 2) {
                continue;
            }

            var owners = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var shape in pair.Value.Values) {
                foreach (var owner in shape) {
                    owners.Add(owner);
                }
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    SchemaNameCollisionDescriptor(),
                    Location.None,
                    pair.Key,
                    string.Join(" and ", owners)));
        }
    }

    /// <summary>Every schema a handler contributes to the document.</summary>
    private static IEnumerable<HandlerSchema> Schemas(RequestHandlerModel handler) {
        if (handler.RequestSchema != null) {
            yield return handler.RequestSchema;
        }

        if (handler.ResponseSchema != null) {
            yield return handler.ResponseSchema;
        }

        foreach (var response in handler.ResponseSchemas) {
            if (response.Schema != null) {
                yield return response.Schema;
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
