using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// A scheme-shape attribute in a position nothing reads.
/// </summary>
/// <remarks>
/// <c>[HttpAuthenticationScheme]</c> and its siblings describe the scheme <em>type</em> that
/// <c>[Authorize&lt;TScheme&gt;]</c> names - <c>SecurityDeclarationSelector</c> reads them from
/// that type and from nowhere else. Applied to a handler, a controller or the application module
/// they publish no <c>securitySchemes</c>, no <c>security</c>, and enforce nothing. The build
/// accepted that silently, and the second trial's code-first arm concluded from the silence that
/// the emission did not exist - the working spelling was never suggested to them.
/// </remarks>
public static class SecuritySchemeDiagnostics {

    public const string MisplacedSchemeId = "HRDSC001";

    internal static DiagnosticDescriptor MisplacedSchemeDescriptor() => new(
        id: MisplacedSchemeId,
        title: "An authentication scheme attribute is not read here",
        messageFormat:
            "'{0}' carries [{1}], which nothing reads in this position. The attribute describes " +
            "an authentication scheme type: declare a class implementing IAuthenticationScheme, " +
            "put the attribute on it, and name it as [Authorize<TScheme>] on the handler or " +
            "controller. Where it is now, it publishes no securitySchemes and enforces nothing.",
        category: "Hardened.Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports each misplaced attribute once, however many handlers saw it.
    /// </summary>
    /// <remarks>
    /// The selector runs per handler and a controller-level attribute is seen by every handler on
    /// the controller, so the entries are deduplicated here on their <c>owner|attribute</c> form.
    /// Decided where the symbols are and reported here, the same split every finding in this
    /// generator uses - a syntax transform cannot report.
    /// </remarks>
    public static void ReportMisplacedSchemes(
        SourceProductionContext context,
        IEnumerable<IReadOnlyList<string>> findings) {
        var reported = new HashSet<string>();

        foreach (var handlerFindings in findings) {
            foreach (var finding in handlerFindings) {
                if (!reported.Add(finding)) {
                    continue;
                }

                var separator = finding.IndexOf('|');

                if (separator <= 0) {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    MisplacedSchemeDescriptor(), Location.None,
                    finding.Substring(0, separator),
                    Short(finding.Substring(separator + 1))));
            }
        }
    }

    /// <summary>The attribute as an author writes it, without the suffix.</summary>
    private static string Short(string attributeTypeName) =>
        attributeTypeName.EndsWith("Attribute", System.StringComparison.Ordinal)
            ? attributeTypeName.Substring(0, attributeTypeName.Length - "Attribute".Length)
            : attributeTypeName;
}
