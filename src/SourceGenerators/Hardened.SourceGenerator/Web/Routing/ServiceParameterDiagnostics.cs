using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// A service typed as its concrete class, read from the request body because of it.
/// </summary>
/// <remarks>
/// <para>
/// CS-08. A parameter that is not a route token and not an interface binds from the body. Typing a
/// service as the class that implements it, rather than the interface it implements, therefore
/// makes it the body parameter - and the build fails as a CS7036 inside
/// <c>obj/**/generated/**</c>, in a file the author did not write, naming neither the convention
/// that decided this nor the parameter whose meaning changed.
/// </para>
/// <para>
/// The rule was narrow because the two shapes are otherwise indistinguishable: a body model is a
/// concrete class too. What separates them is that the deserializer cannot construct an interface,
/// so a type whose every public constructor takes one has no reading as a body at all. That test
/// is made at the syntax transform, where the semantic model is in hand, and carried on
/// <see cref="RequestParameterInformation.ConstructorRequiresServices"/>.
/// </para>
/// <para>
/// The other statement that settles it is the registration. A type carrying
/// <c>[SingletonService]</c>, <c>[ScopedService]</c> or <c>[TransientService]</c> is a service
/// whatever its constructors take, and a body model never carries one - so a parameterless
/// service that passed the constructor test, bound from the body, answered 400 on every request
/// and published a request body on a GET, which is what the 0.20 trial's <c>TodoStore</c> did. It
/// travels on <see cref="RequestParameterInformation.RegisteredAsService"/>.
/// </para>
/// </remarks>
public static class ServiceParameterDiagnostics {

    /// <summary>
    /// <c>HRDR007</c>. <c>HRDR005</c> reports a parameter displaced onto the body by a route token;
    /// this reports one that landed there because of its type.
    /// </summary>
    public const string DiagnosticId = "HRDR007";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>RouteBindingDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these projects
    /// set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Service parameter binds from the request body",
        messageFormat:
            "Parameter '{0}' of '{1}.{2}' is read from the request body. {3}",
        category: "Hardened.Routing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Every body parameter whose type can only be constructed from services, or is registered as
    /// one.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="Report"/> for the reason <c>RouteBindingDiagnostics.Find</c> is: a
    /// <c>SourceProductionContext</c> only exists inside a running generator, and the decision is
    /// worth testing on its own.
    /// </remarks>
    public static IReadOnlyList<RequestParameterInformation> Find(RequestHandlerModel model) {
        List<RequestParameterInformation>? found = null;

        foreach (var parameter in model.RequestParameterInformationList) {
            if (parameter.BindingType == ParameterBindType.Body &&
                (parameter.ConstructorRequiresServices || parameter.RegisteredAsService)) {
                (found ??= new List<RequestParameterInformation>()).Add(parameter);
            }
        }

        return (IReadOnlyList<RequestParameterInformation>?)found
               ?? Array.Empty<RequestParameterInformation>();
    }

    /// <summary>
    /// What to tell the author. Built here rather than in the message format so it can name the
    /// type as well as the parameter.
    /// </summary>
    public static string Advice(RequestParameterInformation parameter) =>
        parameter.RegisteredAsService
            ? $"A parameter that names no route token and is not an interface binds from the " +
              $"body, and '{parameter.ParameterType.Name}' is registered as a service by its " +
              $"[SingletonService], [ScopedService] or [TransientService] attribute, so it was " +
              $"never a body. Mark '{parameter.Name}' [FromServices], or type it as the interface " +
              $"it is registered against."
            : $"A parameter that names no route token and is not an interface binds from the " +
              $"body, and '{parameter.ParameterType.Name}' has no constructor that does not take " +
              $"one, so no body can be read into it. Mark '{parameter.Name}' [FromServices], or " +
              $"type it as the interface it is registered against.";

    /// <summary>Reports every finding, if the handler has any.</summary>
    public static void Report(SourceProductionContext context, RequestHandlerModel model) {
        foreach (var parameter in Find(model)) {
            // Location.None, as everywhere else models are reported from: a syntax location would
            // travel with the model through the incremental caches, which compare models for
            // equality to decide whether to regenerate.
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptor(),
                Location.None,
                parameter.Name,
                model.ControllerType.Name,
                model.HandlerMethod,
                Advice(parameter)));
        }
    }
}
