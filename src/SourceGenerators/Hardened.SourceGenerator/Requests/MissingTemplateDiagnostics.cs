using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// A document that promises rendered markup for a model, and an implementation with no view to
/// render it.
/// </summary>
/// <remarks>
/// <para>
/// This falls out of naming the view on the implementation. The contract and the implementation
/// compile in the same pass, so a disagreement between them is catchable at build time rather than
/// arriving as a 500 on the first request - which is what it was, because nothing serializes an
/// object as <c>text/html</c> and the response would fall through to JSON or to no serializer at
/// all.
/// </para>
/// <para>
/// Scoped to a response that is an object or a list of them. An operation answering
/// <c>text/html</c> with <c>type: string</c> is a handler returning markup it built itself, which
/// needs no view and is a legitimate thing to write.
/// </para>
/// </remarks>
public static class MissingTemplateDiagnostics {
    public const string DiagnosticId = "HOAG020";

    private const string Markup = "text/html";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>UnresolvedHandler.Descriptor</c> is: RS2008 looks for the field, and these projects set
    /// <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Operation declares markup but names no view",
        messageFormat:
        "'{0}.{1}' answers '{2}' with a model, and declares no [Output<T>]. Nothing serializes " +
        "an object as markup, so this would fail at run time. Name the view on the implementation.",
        category: "Hardened.OpenApi",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static void ReportIfMarkupWithoutAView(
        this RequestHandlerModel model, SourceProductionContext context) {
        var response = model.ResponseInformation;

        if (response.OutputType != null ||
            !response.RendersAModel ||
            response.DeclaredContentType == null ||
            !response.DeclaredContentType.StartsWith(Markup, System.StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        // Location.None, as everywhere else models are reported from: a syntax location would
        // travel with the model through the incremental caches, which compare models for equality
        // to decide whether to regenerate.
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor(),
            Location.None,
            model.ControllerType.Name,
            model.HandlerMethod,
            response.DeclaredContentType));
    }
}
