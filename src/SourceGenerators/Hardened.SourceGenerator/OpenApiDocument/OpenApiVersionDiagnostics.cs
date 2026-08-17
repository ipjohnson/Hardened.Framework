using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// What <c>&lt;HardenedOpenApiVersion&gt;</c> reports when it cannot be honoured.
/// </summary>
public static class OpenApiVersionDiagnostics {

    /// <summary>An unrecognised value for the property.</summary>
    public const string UnknownVersionId = "HRDOA001";

    /// <summary>A streamed response under a version with no way to describe one.</summary>
    public const string StreamNeedsItemSchemaId = "HRDOA002";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor UnknownVersionDescriptor() => new(
        id: UnknownVersionId,
        title: "Unrecognised OpenAPI document version",
        messageFormat:
        "<{0}> is '{1}', which is not a version this generator emits. Use 3.0.0, 3.1.0 or 3.2.0, " +
        "or remove the property to take the default of {2}.",
        category: "Hardened.OpenApi",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static DiagnosticDescriptor StreamNeedsItemSchemaDescriptor() => new(
        id: StreamNeedsItemSchemaId,
        title: "Streamed response cannot be described at this document version",
        messageFormat:
        "'{0}' streams its response, and OpenAPI {1} has no way to describe one - itemSchema " +
        "arrived in 3.2. The operation is emitted with its media type and no schema, so the " +
        "document says a body of that type without saying what is in it. Set " +
        "<{2}>3.2.0</{2}> to describe it.",
        category: "Hardened.OpenApi",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports an unrecognised property value.
    /// </summary>
    /// <remarks>
    /// An error, not a fallback. The property exists because somebody's toolchain depends on the
    /// answer, so quietly emitting a different version than was asked for is the one outcome that
    /// cannot be allowed - it would be discovered by their generator, not by this build.
    /// </remarks>
    public static void ReportUnknownVersion(SourceProductionContext context, string configured) {
        context.ReportDiagnostic(
            Diagnostic.Create(
                UnknownVersionDescriptor(),
                Location.None,
                OpenApiVersionFacts.PropertyName,
                configured,
                OpenApiVersionFacts.VersionString(OpenApiVersionFacts.Default)));
    }

    /// <summary>
    /// Reports a streamed handler the selected version cannot describe.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error: the application still builds and still streams correctly,
    /// and someone pinned to 3.0 for a reader that needs it has made a trade rather than a mistake.
    /// What they must not do is believe the document describes the operation.
    /// </remarks>
    public static void ReportStreamNeedsItemSchema(
        SourceProductionContext context, string operation, OpenApiVersion version) {
        context.ReportDiagnostic(
            Diagnostic.Create(
                StreamNeedsItemSchemaDescriptor(),
                Location.None,
                operation,
                OpenApiVersionFacts.VersionString(version),
                OpenApiVersionFacts.PropertyName));
    }
}
