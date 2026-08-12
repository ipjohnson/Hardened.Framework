using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// A handler the generator cannot emit because one of its parameter types does not resolve.
///
/// <para>
/// The rule is that one unbindable signature costs its own handler and nothing else. Before this
/// existed the resolution failure threw out of the syntax transform, which Roslyn reports as
/// <c>CS8785</c> at warning severity and which costs the <em>entire assembly</em> its generated
/// code — every route, every parameter bag and the dependency registration, over one parameter in
/// one method.
/// </para>
///
/// <para>
/// Both output stages have to agree about skipping. Emitting a route for a handler class that was
/// not generated produces a routing table referencing a type that does not exist, which is the
/// uncompilable-output failure the whole test plan exists to prevent.
/// </para>
/// </summary>
public static class UnresolvedHandler {

    /// <summary>
    /// Reported instead of the crash. Deliberately <em>not</em> a restatement of the compiler's
    /// own <c>CS0246</c> — the user already knows the type does not exist. This says the part the
    /// compiler cannot: that the handler was dropped as a result, which is why the route stopped
    /// existing.
    /// </summary>
    public const string DiagnosticId = "HOAG010";

    /// <summary>
    /// Built per call rather than held in a static field. A field is what RS2008 looks for, and
    /// these projects set <c>EnforceExtendedAnalyzerRules</c>, so a static descriptor demands
    /// AnalyzerReleases tracking files in all five wrapper projects that link this source. The
    /// existing <c>HardenedException</c> descriptor is constructed inline for the same reason.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Handler not generated",
        messageFormat:
        "'{0}.{1}' was not generated because the type of parameter '{2}' could not be resolved. " +
        "Other handlers in this assembly are unaffected.",
        category: "Hardened.Generation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>The first parameter that did not resolve, or null when the handler is fine.</summary>
    public static RequestParameterInformation? UnresolvedParameter(this RequestHandlerModel model) {
        foreach (var parameter in model.RequestParameterInformationList) {
            if (parameter.BindingType == ParameterBindType.Unresolved) {
                return parameter;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the handler was skipped, having reported why. Callers emit nothing further.
    /// </summary>
    public static bool ReportIfUnresolved(this RequestHandlerModel model, SourceProductionContext context) {
        var parameter = model.UnresolvedParameter();

        if (parameter == null) {
            return false;
        }

        // Location.None rather than the parameter's own span: a syntax location in the model would
        // travel with it through the incremental caches, and models are compared for equality to
        // decide whether to regenerate. The message names the handler and parameter instead.
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor(),
            Location.None,
            model.ControllerType.Name,
            model.HandlerMethod,
            parameter.Name));

        return true;
    }
}
