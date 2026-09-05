using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// A request body read where there is none to read: two parameters that both fell to it, or one
/// on a verb that carries no body.
/// </summary>
/// <remarks>
/// <para>
/// A parameter that names no route token and is not an interface binds from the body. Two of them
/// on one handler are two readings of a body the request carries once, and the bridge that
/// rebuilds the parameter list keeps the first - so the second was dropped from the generated
/// invocation and the build failed as a CS7036 inside <c>obj/**/generated/**</c>, naming neither
/// the parameter nor the convention that discarded it. That is <c>HRDR009</c>, an error.
/// </para>
/// <para>
/// One body parameter on a GET is the same convention landing somewhere the request cannot
/// follow. The handler compiled, the route matched, and every request answered 400 "The input
/// does not contain any JSON tokens" - while the published document gave the GET a request body
/// and put the parameter's type in its schemas. <c>HRDR005</c> reports this where a route token
/// displaced the parameter and <c>HRDR007</c> where the type is a service; <c>HRDR010</c> is the
/// remainder, a warning, because a GET carrying a body is a thing some APIs deliberately do and
/// <c>NoWarn</c> is how they say so.
/// </para>
/// </remarks>
public static class BodyParameterDiagnostics {

    /// <summary>
    /// <c>HRDR009</c>. <c>HRDR007</c> reports a body parameter that is a service; this reports a
    /// second body parameter of any kind.
    /// </summary>
    public const string SeveralBodiesDiagnosticId = "HRDR009";

    /// <summary>
    /// <c>HRDR010</c>. <c>HRDR005</c> reports a body parameter a route token displaced; this
    /// reports one on a verb that carries no body, whatever put it there.
    /// </summary>
    public const string BodylessVerbDiagnosticId = "HRDR010";

    /// <summary>Verbs whose requests carry no body to read a parameter out of. As HRDR005 lists them.</summary>
    private static readonly HashSet<string> BodylessVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>RouteBindingDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these projects
    /// set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor SeveralBodies() => new(
        id: SeveralBodiesDiagnosticId,
        title: "More than one parameter binds from the request body",
        messageFormat:
        "'{0}.{1}' reads {2} from the request body, and a request carries one body. A parameter " +
        "that names no route token and is not an interface binds from the body: mark a service " +
        "[FromServices] or type it as the interface it is registered against, and bind a value " +
        "with [FromQueryString], [FromHeader], [FromForm] or a route token.",
        category: "Hardened.Routing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static DiagnosticDescriptor BodylessVerb() => new(
        id: BodylessVerbDiagnosticId,
        title: "Parameter binds from the body of a request that carries none",
        messageFormat:
        "Parameter '{0}' of '{1}.{2}' is read from the request body, and a {3} carries none, so a " +
        "request that sends no body is refused before the handler runs and the published " +
        "document gives the operation a body it should not have. Bind '{0}' with " +
        "[FromQueryString] or [FromHeader], mark it [FromServices] if it is a service, or " +
        "suppress " + BodylessVerbDiagnosticId + " if this operation deliberately reads a body " +
        "from a {3}.",
        category: "Hardened.Routing",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// The body parameters a bodyless verb reads that nothing else reports.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Report"/> because a <c>SourceProductionContext</c> only exists
    /// inside a running generator, and the decision is worth testing on its own. A parameter
    /// <c>HRDR005</c> or <c>HRDR007</c> already names is left to that report, which says more
    /// about why it is where it is.
    /// </remarks>
    public static IReadOnlyList<RequestParameterInformation> FindOnBodylessVerb(RequestHandlerModel model) {
        if (model.Name.IsDispatched || !BodylessVerbs.Contains(model.Name.Method)) {
            return Array.Empty<RequestParameterInformation>();
        }

        HashSet<string>? displaced = null;

        foreach (var finding in RouteBindingDiagnostics.Find(model)) {
            (displaced ??= new HashSet<string>(StringComparer.Ordinal)).Add(finding.BodyParameter);
        }

        List<RequestParameterInformation>? found = null;

        foreach (var parameter in model.RequestParameterInformationList) {
            if (parameter.BindingType != ParameterBindType.Body ||
                parameter.ConstructorRequiresServices ||
                parameter.RegisteredAsService ||
                displaced?.Contains(parameter.Name) == true) {
                continue;
            }

            (found ??= new List<RequestParameterInformation>()).Add(parameter);
        }

        return (IReadOnlyList<RequestParameterInformation>?)found
               ?? Array.Empty<RequestParameterInformation>();
    }

    /// <summary>Reports both findings, if the handler has either.</summary>
    public static void Report(SourceProductionContext context, RequestHandlerModel model) {
        // Location.None, as everywhere else models are reported from: a syntax location would
        // travel with the model through the incremental caches, which compare models for
        // equality to decide whether to regenerate. The message carries the handler instead.
        if (model.AdditionalBodyParameters.Count > 0) {
            context.ReportDiagnostic(Diagnostic.Create(
                SeveralBodies(),
                Location.None,
                model.ControllerType.Name,
                model.HandlerMethod,
                BodyNames(model)));
        }

        foreach (var parameter in FindOnBodylessVerb(model)) {
            context.ReportDiagnostic(Diagnostic.Create(
                BodylessVerb(),
                Location.None,
                parameter.Name,
                model.ControllerType.Name,
                model.HandlerMethod,
                model.Name.Method.ToUpperInvariant()));
        }
    }

    /// <summary>Every body parameter, quoted: "'counter' and 'reading'".</summary>
    private static string BodyNames(RequestHandlerModel model) {
        var names = new List<string>();

        foreach (var parameter in model.RequestParameterInformationList) {
            if (parameter.BindingType == ParameterBindType.Body) {
                names.Add("'" + parameter.Name + "'");

                break;
            }
        }

        foreach (var name in model.AdditionalBodyParameters) {
            names.Add("'" + name + "'");
        }

        return names.Count == 2
            ? names[0] + " and " + names[1]
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
    }
}
