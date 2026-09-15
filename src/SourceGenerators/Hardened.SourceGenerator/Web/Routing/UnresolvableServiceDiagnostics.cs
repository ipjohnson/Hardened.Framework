using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// A service parameter typed as a class the container is not asked to build.
/// </summary>
/// <remarks>
/// <para>
/// A class carrying <c>[SingletonService]</c>, <c>[ScopedService]</c> or
/// <c>[TransientService]</c> is registered against one service type, and that is an interface
/// wherever the class declares one. The class itself is not registered, so
/// <c>[FromServices] TodoStore</c> compiles, publishes nothing unusual, and throws
/// <c>No service for type 'TodoStore' has been registered</c> inside the generated binder on the
/// first request.
/// </para>
/// <para>
/// Reported here rather than left to run time because the build has both halves in hand: the
/// parameter names the class, and the class's own attributes say what it is registered against.
/// <c>HRDR007</c> is the neighbouring finding and used to send authors straight into this one - it
/// offered <c>[FromServices]</c> as the first of its two fixes, which is the one that fails
/// whenever the service has an interface.
/// </para>
/// <para>
/// See <see cref="RequestParameterInformation.ServiceRegisteredAs"/> for what the build can say
/// about the registration without restating DependencyModules' own inference rule.
/// </para>
/// </remarks>
public static class UnresolvableServiceDiagnostics
{
    public const string DiagnosticId = "HRDR015";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>ServiceParameterDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() =>
        new(
            id: DiagnosticId,
            title: "Service parameter is typed as a class nothing resolves",
            messageFormat: "Parameter '{0}' of '{1}.{2}' is resolved from the container as '{3}', which is "
                + "registered against {4} rather than against itself - so this throws on the first request. "
                + "Type the parameter as {4}, or carry [CrossWireService] on '{3}' to register the class and "
                + "point the interfaces at it.",
            category: "Hardened.Routing",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

    /// <summary>
    /// Every service parameter whose type is registered against something else.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="Report"/> for the reason <c>ServiceParameterDiagnostics.Find</c> is: a
    /// <c>SourceProductionContext</c> only exists inside a running generator, and the decision is
    /// worth testing on its own.
    /// </remarks>
    public static IReadOnlyList<RequestParameterInformation> Find(RequestHandlerModel model)
    {
        List<RequestParameterInformation>? found = null;

        foreach (var parameter in model.RequestParameterInformationList)
        {
            if (
                parameter.BindingType == ParameterBindType.FromServiceProvider
                && parameter.ServiceRegisteredAs != null
            )
            {
                (found ??= new List<RequestParameterInformation>()).Add(parameter);
            }
        }

        return (IReadOnlyList<RequestParameterInformation>?)found
            ?? Array.Empty<RequestParameterInformation>();
    }

    /// <summary>
    /// What the parameter should be typed as, named where the build can name it.
    /// </summary>
    /// <remarks>
    /// The interface where the class declares exactly one, and "the interface it implements"
    /// otherwise. Which of several DependencyModules picks is its rule to state, and naming the
    /// wrong one would send the author to a second failure.
    /// </remarks>
    public static string Registration(RequestParameterInformation parameter) =>
        string.IsNullOrEmpty(parameter.ServiceRegisteredAs)
            ? "the interface it implements"
            : "'" + parameter.ServiceRegisteredAs + "'";

    /// <summary>Reports every finding, if the handler has any.</summary>
    public static void Report(SourceProductionContext context, RequestHandlerModel model)
    {
        foreach (var parameter in Find(model))
        {
            // Location.None, as everywhere else models are reported from: a syntax location would
            // travel with the model through the incremental caches, which compare models for
            // equality to decide whether to regenerate.
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Descriptor(),
                    Location.None,
                    parameter.Name,
                    model.ControllerType.Name,
                    model.HandlerMethod,
                    parameter.ParameterType.Name,
                    Registration(parameter)
                )
            );
        }
    }
}
