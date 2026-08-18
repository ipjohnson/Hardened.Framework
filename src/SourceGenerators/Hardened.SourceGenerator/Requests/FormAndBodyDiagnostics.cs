using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// A handler that binds form fields and a body model at once.
/// </summary>
/// <remarks>
/// <para>
/// There is one body, and the two readings of it are different: <c>[FromForm]</c> reads it as
/// <c>name=value&amp;name=value</c>, a body parameter hands the same bytes to a deserializer. One
/// of them gets the stream and the other gets what is left, which on a non-seekable body is
/// nothing.
/// </para>
/// <para>
/// <b>An error rather than a warning, and rather than an ordering rule.</b> The alternative is
/// deciding which one wins and documenting it, which makes a handler's behaviour depend on
/// something nobody reading the signature can see. The signature says the author wanted both, and
/// there is no arrangement of the pipeline that gives them both.
/// </para>
/// <para>
/// Reported here rather than left to run time because the failure is otherwise a silently empty
/// model or a silently empty set of fields, on a handler that compiles and routes correctly.
/// </para>
/// </remarks>
public static class FormAndBodyDiagnostics {
    public const string DiagnosticId = "HRDW002";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Handler binds both a form and a body",
        messageFormat:
        "'{0}' binds '{1}' with [FromForm] and '{2}' from the request body. There is one body and " +
        "the two read it differently, so whichever runs second sees a consumed stream. Bind the " +
        "fields individually with [FromForm], or take the body as a model - not both.",
        category: "Hardened.Web",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// The offending pair, or null when the handler binds at most one of the two.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Report"/> because a <c>SourceProductionContext</c> only exists
    /// inside a running generator, and the decision this makes is worth testing on its own. Same
    /// split as <c>AmbiguousRouteDiagnostics.Severity</c>.
    /// </remarks>
    public static (RequestParameterInformation Form, RequestParameterInformation Body)? FindConflict(
        RequestHandlerModel model) {
        RequestParameterInformation? form = null;
        RequestParameterInformation? body = null;

        foreach (var parameter in model.RequestParameterInformationList) {
            if (form == null && parameter.BindingType == ParameterBindType.Form) {
                form = parameter;
            }
            else if (body == null && parameter.BindingType == ParameterBindType.Body) {
                body = parameter;
            }
        }

        return form != null && body != null ? (form, body) : null;
    }

    /// <summary>
    /// Reports the combination, if the handler has it.
    /// </summary>
    public static void Report(SourceProductionContext context, RequestHandlerModel model) {
        var conflict = FindConflict(model);

        if (conflict == null) {
            return;
        }

        var (form, body) = conflict.Value;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Descriptor(),
                Location.None,
                model.ControllerType.Name + "." + model.HandlerMethod,
                form.Name,
                body.Name));
    }
}
