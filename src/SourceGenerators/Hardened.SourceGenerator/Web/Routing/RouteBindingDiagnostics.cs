using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// A route token that binds nothing, beside a parameter read from a body the request does not
/// carry.
/// </summary>
/// <remarks>
/// <para>
/// <c>[Get("/{eventid}")]</c> against a parameter called <c>eventId</c> built with zero warnings.
/// The token bound nothing, the parameter fell through to the body branch, and a GET answered 400
/// "input does not contain any JSON tokens" at request time - from a route that matched perfectly.
/// The generator has both lists in hand.
/// </para>
/// <para>
/// <b>Both halves are required, which is deliberate.</b> A token that binds nothing is not a
/// mistake on its own: a token declared in a <c>[BasePath]</c> and used by only some of the
/// handlers under it binds nothing on the rest, and that is a route matching more than one handler
/// needs, not a defect. What makes it a defect is the parameter that fell somewhere else because
/// of it - either onto a body the verb does not carry, or, whatever the verb, onto a body when its
/// name differs from the token only by case. A case-only difference is never what anyone meant.
/// </para>
/// </remarks>
public static class RouteBindingDiagnostics {

    /// <summary>
    /// <c>HRDR005</c>. <c>HRDR002</c> reports a token Hardened does not compile; this reports one
    /// it compiles and nothing binds.
    /// </summary>
    public const string DiagnosticId = "HRDR005";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>FormAndBodyDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these projects
    /// set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Route token binds no parameter",
        messageFormat: "Route '{0}' on '{1}.{2}' declares '{{{3}}}', which no parameter binds. {4}",
        category: "Hardened.Routing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>The token that binds nothing, and the parameter that went to the body instead.</summary>
    public readonly struct Finding {
        public Finding(string token, string bodyParameter, bool caseOnly) {
            Token = token;
            BodyParameter = bodyParameter;
            CaseOnly = caseOnly;
        }

        /// <summary>The token's name, braces and any constraint stripped.</summary>
        public string Token { get; }

        /// <summary>The parameter being read from the request body.</summary>
        public string BodyParameter { get; }

        /// <summary>Whether the two differ only by case.</summary>
        public bool CaseOnly { get; }
    }

    /// <summary>Verbs whose requests carry no body to read a parameter out of.</summary>
    /// <remarks>
    /// DELETE is not among them. HTTP permits a body on one and some APIs send it, so a body
    /// parameter there is a choice rather than a mistake - and a DELETE with the trial's typo is
    /// still reported, through the case-only half of the rule.
    /// </remarks>
    private static readonly HashSet<string> BodylessVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    /// <summary>
    /// Every token that binds nothing while a parameter is read from the body it displaced.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Report"/> because a <c>SourceProductionContext</c> only exists
    /// inside a running generator, and the decision this makes is worth testing on its own. Same
    /// split as <c>FormAndBodyDiagnostics.FindConflict</c>.
    /// </remarks>
    public static IReadOnlyList<Finding> Find(RequestHandlerModel model) {
        // A dispatched handler is selected by an exact token in a header, so its path declares
        // nothing to bind - awsJson sends every operation to POST /.
        if (model.Name.IsDispatched) {
            return Array.Empty<Finding>();
        }

        var tokens = RouteTokens.Names(model.Name.Path);

        if (tokens.Count == 0) {
            return Array.Empty<Finding>();
        }

        List<string>? unbound = null;

        foreach (var token in tokens) {
            if (!Binds(model, token)) {
                (unbound ??= new List<string>()).Add(token);
            }
        }

        if (unbound == null) {
            return Array.Empty<Finding>();
        }

        var bodyless = BodylessVerbs.Contains(model.Name.Method);
        List<Finding>? findings = null;

        foreach (var token in unbound) {
            foreach (var parameter in model.RequestParameterInformationList) {
                if (parameter.BindingType != ParameterBindType.Body) {
                    continue;
                }

                var caseOnly = string.Equals(parameter.Name, token, StringComparison.OrdinalIgnoreCase);

                if (!caseOnly && !bodyless) {
                    continue;
                }

                (findings ??= new List<Finding>()).Add(new Finding(token, parameter.Name, caseOnly));

                break;
            }
        }

        return (IReadOnlyList<Finding>?)findings ?? Array.Empty<Finding>();
    }

    /// <summary>
    /// Whether any parameter binds this token. Read the way the binder reads it: a described
    /// parameter carries the wire name the route declares and a C# identifier that may differ, and
    /// a code-first one carries only the identifier.
    /// </summary>
    private static bool Binds(RequestHandlerModel model, string token) {
        foreach (var parameter in model.RequestParameterInformationList) {
            if (parameter.BindingType != ParameterBindType.Path) {
                continue;
            }

            var bound = string.IsNullOrEmpty(parameter.BindingName)
                ? parameter.Name
                : parameter.BindingName;

            if (string.Equals(bound, token, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What to tell the author, built here rather than in the message format so it can carry both
    /// identifiers.
    /// </summary>
    public static string Advice(RequestHandlerModel model, Finding finding) {
        if (finding.CaseOnly) {
            return
                $"'{finding.BodyParameter}' differs from it only by case, so it is read from the " +
                $"request body instead. Route tokens bind by exact name: spell the token " +
                $"'{{{finding.BodyParameter}}}', or rename the parameter to '{finding.Token}'.";
        }

        return
            $"'{finding.BodyParameter}' matches no token either, so it is read from the request " +
            $"body - and a {model.Name.Method} carries none, so every request is refused before " +
            $"the handler runs. Name a parameter '{finding.Token}', or bind " +
            $"'{finding.BodyParameter}' with [FromQueryString] or [FromHeader].";
    }

    /// <summary>Reports every finding, if the handler has any.</summary>
    public static void Report(SourceProductionContext context, RequestHandlerModel model) {
        foreach (var finding in Find(model)) {
            // Location.None, as everywhere else models are reported from: a syntax location would
            // travel with the model through the incremental caches, which compare models for
            // equality to decide whether to regenerate. The message carries the route and handler
            // instead.
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptor(),
                Location.None,
                model.Name.Path,
                model.ControllerType.Name,
                model.HandlerMethod,
                finding.Token,
                Advice(model, finding)));
        }
    }
}
