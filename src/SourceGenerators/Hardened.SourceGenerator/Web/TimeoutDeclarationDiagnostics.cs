using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// A <c>[Timeout]</c> whose budget is zero or less.
/// </summary>
/// <remarks>
/// <para>
/// The runtime refuses it - <c>TimeoutResolver</c> fails as the handler's chain is composed,
/// naming the handler and the rung - but that is the first request, answered 500. And the
/// document export ran ahead of it: an operation under <c>[Timeout(Milliseconds = 0)]</c>
/// published <c>x-hardened-timeout: 0</c>, a contract that fails on the first request of any
/// service regenerated from it.
/// </para>
/// <para>
/// An error with no <c>NoWarn</c>, like <see cref="CompressDiagnostics"/>: a handler that should
/// not be bounded declares no timeout, and there is no reading of zero that means anything else.
/// </para>
/// </remarks>
public static class TimeoutDeclarationDiagnostics {
    public const string DiagnosticId = "HRDW006";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "[Timeout] declares no budget",
        messageFormat:
        "'{0}' is bounded by a [Timeout] declaring {1} milliseconds, on the operation, its class " +
        "or its assembly. A budget has to be greater than zero; a handler that should not be " +
        "bounded declares no timeout instead. The runtime refuses this on the first request, and " +
        "the document would publish x-hardened-timeout: {1}.",
        category: "Hardened.Web",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static void Report(SourceProductionContext context, RequestHandlerModel model) {
        if (model.DeclaredTimeout is not { Milliseconds: <= 0 } declared) {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Descriptor(),
                Location.None,
                model.ControllerType.Name + "." + model.HandlerMethod,
                declared.Milliseconds));
    }
}
