using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// What an operation says it produces, checked against what could produce it.
/// </summary>
/// <remarks>
/// <para>
/// Two findings, at two severities, and the difference is what the build can know. A handler
/// returning <c>byte[]</c> or <c>Stream</c> with no declaration is a fault entirely inside the code
/// that wrote it: bytes have no media type anyone could infer, and nothing downstream can supply
/// one. That is an error.
/// </para>
/// <para>
/// A declared media type nothing visible produces is a warning, because a library declaring
/// <c>text/csv</c> has no way to know whether its host will register a CSV serializer - and a
/// library that cannot compile without its host is not a library. The entry point is the only place
/// the full registration set exists, and that is where strictness could be raised later if it earns
/// it. <c>ContentTypeNotProducibleException</c> still answers the case that reaches run time.
/// </para>
/// <para>
/// Found in the syntax transform, where the return type is known, and reported from the routing
/// generator, which has a <c>SourceProductionContext</c>. At <see cref="Location.None"/> for the
/// reason its neighbours are: the handler model carries no location, by design, because a span on
/// it would rebuild every handler below an edit.
/// </para>
/// </remarks>
public static class ContentTypeDiagnostics {
    public const string MissingDeclarationId = "HRDR011";

    public const string NothingProducesId = "HRDR012";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>StreamFramingDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor MissingDeclaration() => new(
        id: MissingDeclarationId,
        title: "handler returns bytes and declares no content type",
        messageFormat:
        "'{0}' returns byte[] or Stream and carries no [Produces], so nothing says what the bytes " +
        "are. Returning either means the handler writes its own response, and no serializer is " +
        "consulted - declare the media type with [Produces(\"application/pdf\")] or return a model.",
        category: "Hardened.Web",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static DiagnosticDescriptor NothingProduces() => new(
        id: NothingProducesId,
        title: "nothing in this compilation produces a declared content type",
        messageFormat:
        "'{0}' declares [Produces(\"{1}\")] and returns a model, and nothing here writes a model as " +
        "that media type. Register an IResponseSerializer declaring it, or return string, byte[] " +
        "or Stream and write the bytes yourself. A host that registers one makes this correct, " +
        "which is why it is a warning.",
        category: "Hardened.Web",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports the findings the transform carried, if there are any.
    /// </summary>
    /// <param name="unproducible">
    /// The declared media types nothing can write, comma-joined, as the transform found them.
    /// </param>
    public static void Report(
        SourceProductionContext context,
        string handler,
        bool declaresNothing,
        string? unproducible) {
        if (declaresNothing) {
            context.ReportDiagnostic(
                Diagnostic.Create(MissingDeclaration(), Location.None, handler));
        }

        if (string.IsNullOrEmpty(unproducible)) {
            return;
        }

        foreach (var contentType in unproducible!.Split(',')) {
            context.ReportDiagnostic(
                Diagnostic.Create(NothingProduces(), Location.None, handler, contentType));
        }
    }
}
