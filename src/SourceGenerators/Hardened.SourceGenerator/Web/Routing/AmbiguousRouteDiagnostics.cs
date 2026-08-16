using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// Two routes at the same position differing only by what their token accepts.
/// </summary>
/// <remarks>
/// <para>
/// <c>[Get("/users/{id:int}")]</c> beside <c>[Get("/users/{name}")]</c>. Overloading by type makes
/// which handler you reach depend on the <em>content</em> of a value: a user named <c>12345</c>
/// becomes unreachable, a client cannot reason about which endpoint it hit, and caches cannot tell
/// the two apart. It is also unrepresentable in OpenAPI - one path, one operation per verb - so it
/// is at war with everything the document work is for. The same rule applies to <c>{name}</c>
/// versus <c>{*name}</c>, where the two differ only in how much of the path the token takes.
/// </para>
/// <para>
/// Literal-versus-token is untouched: <c>/users/me</c> beside <c>/users/{id}</c> is an ordinary
/// thing to write, the literal wins, and a document describes both.
/// </para>
/// <para>
/// It has a diagnostic ID so the opinion can be overridden by the mechanism every .NET developer
/// already knows:
/// </para>
/// <code>
/// dotnet_diagnostic.HRDR001.severity = warning
/// </code>
/// <para>
/// which does something a csproj property cannot: per-file severity, so one legacy route pair can
/// be allowed in one controller without opening the gate project-wide. Prefer <c>warning</c> over
/// <c>none</c> - an override that silences leaves no record that the codebase drifted, and since
/// CI runs <c>TreatWarningsAsErrors</c> an opt-in still forces a deliberate decision about CI,
/// which is where that conversation belongs.
/// </para>
/// </remarks>
public static class AmbiguousRouteDiagnostics {
    public const string DiagnosticId = "HRDR001";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>UnresolvedHandler.Descriptor</c> is: RS2008 looks for the field, and these projects set
    /// <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor(DiagnosticSeverity severity) => new(
        id: DiagnosticId,
        title: "Ambiguous route pair",
        messageFormat:
        "'{0}' and '{1}' match the same paths and differ only in what their token accepts, so which " +
        "handler a request reaches depends on the value in it. A client cannot reason about which " +
        "endpoint it hit, caches cannot tell them apart, and the pair is unrepresentable in OpenAPI. " +
        "Give them different paths.",
        category: "Hardened.Routing",
        defaultSeverity: severity,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports every ambiguous pair among <paramref name="handlers"/>.
    /// </summary>
    /// <param name="defaultSeverity">
    /// From <c>&lt;HardenedAmbiguousRoutes&gt;</c>, which layers on top of the per-file
    /// <c>.editorconfig</c> mechanism for discoverability. Error unless the project says otherwise.
    /// </param>
    public static void ReportAmbiguousRoutes(
        SourceProductionContext context,
        IReadOnlyList<RequestHandlerModel> handlers,
        string basePath,
        DiagnosticSeverity defaultSeverity) {
        // Grouped by the shape a request is matched against: the template with every token reduced
        // to a marker, plus the verb. Two routes in one group match exactly the same requests, so
        // anything distinguishing them can only be the content of a token.
        var byShape = new Dictionary<string, List<(string Route, IReadOnlyList<string> Tokens)>>(
            StringComparer.Ordinal);

        foreach (var handler in handlers) {
            var route = RoutePath.Combine(basePath, handler.Name.Path);
            var (shape, tokens) = RouteTreeGenerator<RequestHandlerModel>.StandardizeToken(route);
            var key = handler.Name.Method + " " + shape;

            if (!byShape.TryGetValue(key, out var group)) {
                group = new List<(string, IReadOnlyList<string>)>();
                byShape[key] = group;
            }

            group.Add((route, tokens));
        }

        foreach (var group in byShape) {
            if (group.Value.Count < 2) {
                continue;
            }

            for (var i = 0; i < group.Value.Count; i++) {
                for (var j = i + 1; j < group.Value.Count; j++) {
                    if (!DiffersOnlyByWhatTheTokenAccepts(group.Value[i].Tokens, group.Value[j].Tokens)) {
                        continue;
                    }

                    // Location.None, as everywhere else models are reported from: a syntax location
                    // would travel with the model through the incremental caches, which compare
                    // models for equality to decide whether to regenerate.
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptor(defaultSeverity),
                        Location.None,
                        group.Value[i].Route,
                        group.Value[j].Route));
                }
            }
        }
    }

    /// <summary>
    /// Whether two token lists for the same path shape differ in a constraint or a catch-all
    /// marker.
    /// </summary>
    /// <remarks>
    /// Names are ignored. Two routes whose tokens differ only in name are a duplicate route rather
    /// than an ambiguous pair - a different problem, with a different answer, and not one this
    /// rule is about.
    /// </remarks>
    private static bool DiffersOnlyByWhatTheTokenAccepts(
        IReadOnlyList<string> left, IReadOnlyList<string> right) {
        if (left.Count != right.Count) {
            return false;
        }

        for (var i = 0; i < left.Count; i++) {
            if (RouteTokens.IsCatchAll(left[i]) != RouteTokens.IsCatchAll(right[i]) ||
                !string.Equals(
                    RouteTokens.Constraint(left[i]), RouteTokens.Constraint(right[i]),
                    StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The severity <c>&lt;HardenedAmbiguousRoutes&gt;</c> asks for, or error.
    /// </summary>
    public static DiagnosticSeverity Severity(string? configured) =>
        configured?.Trim().ToLowerInvariant() switch {
            "warning" => DiagnosticSeverity.Warning,
            "info" or "suggestion" => DiagnosticSeverity.Info,
            "none" or "hidden" => DiagnosticSeverity.Hidden,
            _ => DiagnosticSeverity.Error
        };
}
