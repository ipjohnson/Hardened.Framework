using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// A <c>[FromForm]</c> or <c>[FromQueryString]</c> parameter whose type is a model the binder
/// cannot build one field at a time.
/// </summary>
/// <remarks>
/// <para>
/// Before models were bound member by member, such a parameter compiled and bound as one field
/// named after the parameter. The string converter cannot read a model from one string, so every
/// request answered 400 naming a field no client was ever told to send.
/// </para>
/// <para>
/// <b>An error, and emitted anyway.</b> The binder falls back to that one-field reading, so the
/// generated code compiles beside the diagnostic and the build fails on the one error that says
/// what is wrong.
/// </para>
/// </remarks>
public static class BoundModelDiagnostics
{
    public const string DiagnosticId = "HRDW007";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>FormAndBodyDiagnostics.Descriptor</c> is.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() =>
        new(
            id: DiagnosticId,
            title: "A form or query string model cannot be bound",
            messageFormat: "'{0}' binds '{1}' from the {2} one field per member, but {3}",
            category: "Hardened.Web",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

    /// <summary>
    /// Every parameter whose model has a problem, with the problem.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Report"/> for the reason <c>FormAndBodyDiagnostics.FindConflict</c>
    /// is.
    /// </remarks>
    public static IReadOnlyList<(
        RequestParameterInformation Parameter,
        string Problem
    )> FindProblems(RequestHandlerModel model)
    {
        var problems = new List<(RequestParameterInformation, string)>();

        foreach (var parameter in model.RequestParameterInformationList)
        {
            if (parameter.Model?.Problem is { } problem)
            {
                problems.Add((parameter, problem));
            }
        }

        return problems;
    }

    public static void Report(SourceProductionContext context, RequestHandlerModel model)
    {
        foreach (var (parameter, problem) in FindProblems(model))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Descriptor(),
                    Location.None,
                    model.ControllerType.Name + "." + model.HandlerMethod,
                    parameter.Name,
                    parameter.BindingType == ParameterBindType.Form ? "form" : "query string",
                    problem + "."
                )
            );
        }
    }
}
