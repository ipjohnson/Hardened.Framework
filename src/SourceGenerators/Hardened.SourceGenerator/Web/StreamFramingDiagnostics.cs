using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// <c>[ServerSentEvents]</c> on a handler that does not return <c>IAsyncEnumerable&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The attribute names a framing for a stream, and a handler that returns anything else has no
/// stream to frame. The emitter branches on the return type first and ignores the framing on such
/// a handler, so without this the author gets a buffered JSON response, a document that says so,
/// and nothing that says why. The attribute's own doc has promised a build error since it existed;
/// this is that error.
/// </para>
/// <para>
/// An error rather than a warning, like <see cref="CompressDiagnostics"/>: there is no
/// configuration under which the attribute means anything on that handler. Found in the syntax
/// transform, where the return type is known, and reported from the routing generator, which has a
/// <c>SourceProductionContext</c> - the route <c>ResponseModelDiagnostics</c> takes. At
/// <see cref="Location.None"/> for the reason its neighbours are: the handler model carries no
/// location, by design, because a span on it would rebuild every handler below an edit.
/// </para>
/// </remarks>
public static class StreamFramingDiagnostics {
    public const string DiagnosticId = "HRDW004";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "[ServerSentEvents] on a handler that does not return IAsyncEnumerable<T>",
        messageFormat:
        "'{0}' carries [ServerSentEvents] but does not return IAsyncEnumerable<T>, so there is no " +
        "stream to frame and the response is buffered and serialized as JSON. Return " +
        "IAsyncEnumerable<T> to stream it as text/event-stream, or remove the attribute.",
        category: "Hardened.Web",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports the finding the transform carried, if there is one.
    /// </summary>
    public static void Report(SourceProductionContext context, string handler, string? finding) {
        if (string.IsNullOrEmpty(finding)) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptor(), Location.None, handler));
    }
}
