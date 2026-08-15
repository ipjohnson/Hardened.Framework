using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// Reports the brace forms <see cref="RouteTokenSyntax"/> classifies as unsupported.
///
/// <para>
/// Reported from the per-handler generator rather than from the routing table, for the reason
/// <c>UnresolvedHandler</c> is: the table sees every handler at once and would repeat itself for
/// each entry point in the assembly.
/// </para>
///
/// <para>
/// Both routing generators go through that stage, so a specification-first application is held to
/// the same rule. That is not scope creep - the route tree, and so <c>StandardizeToken</c>, is
/// shared, which means a <c>{id:int}</c> in an OpenAPI document is broken in precisely the way it
/// is broken in an attribute. A document that really did intend a parameter named <c>id:int</c>
/// has to rename it, which it would have to do for any client generator anyway.
/// </para>
/// </summary>
public static class RouteTokenDiagnostics {

    /// <summary>
    /// <c>HRDR002</c>, not <c>001</c>. <c>HRDR001</c> is the ambiguous-route-pair rule, whose ID is
    /// published in documentation as the thing an <c>.editorconfig</c> line names - so it is
    /// reserved rather than assigned in the order the two happened to be built.
    /// </summary>
    public const string DiagnosticId = "HRDR002";

    /// <summary>
    /// Built per call rather than held in a static field: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>. Same reason as
    /// <c>UnresolvedHandler.Descriptor</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Unsupported route token syntax",
        messageFormat: "Route '{0}' on '{1}.{2}' declares '{3}', which Hardened does not compile. {4}",
        category: "Hardened.Routing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// True when the route declared a token Hardened does not compile, having reported each one.
    /// </summary>
    public static bool ReportUnsupportedTokens(
        this RequestHandlerModel model, SourceProductionContext context) {
        var findings = RouteTokenSyntax.Scan(model.Name.Path);

        foreach (var finding in findings) {
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
                RouteTokenSyntax.Advice(finding)));
        }

        return findings.Count > 0;
    }
}
