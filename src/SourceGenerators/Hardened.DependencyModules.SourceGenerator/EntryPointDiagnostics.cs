using System.Collections.Immutable;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace Hardened.DependencyModules.SourceGenerator;

/// <summary>
/// More than one <c>[HardenedModule]</c> entry point in one assembly.
/// </summary>
/// <remarks>
/// <para>
/// It compiles, and until now it did so silently. Every generator scoped per entry point runs again
/// for the second one: a routing table, a links type, a template base and the DI registrations, all
/// duplicated - and every entry point sees <em>every</em> handler in the compilation, because route
/// providers are collected per compilation and combined with each entry point in turn. Two entry
/// points in one assembly are therefore two applications with identical routes, differing only in
/// what modules they compose.
/// </para>
/// <para>
/// That is a real arrangement - the same API hosted two ways - and a rare one against how often a
/// second <c>[HardenedModule]</c> is a leftover, a copy-paste, or an attribute that landed on the
/// wrong class. The cost of the mistake is doubled generated code and no signal at all about which
/// entry point a host is running, so it is reported rather than assumed to be deliberate.
/// </para>
/// <para>
/// Where routes are genuinely meant to be shared, the arrangement is a <c>[WebLibrary]</c> in its
/// own project, which an application opts into by applying the library's generated module attribute.
/// That is what partitions routes; a second entry point beside them does not.
/// </para>
/// <para>
/// An error by default, suppressible either way a .NET developer already knows:
/// </para>
/// <code>
/// &lt;NoWarn&gt;$(NoWarn);HRDR004&lt;/NoWarn&gt;        &lt;!-- project-wide, in the csproj --&gt;
/// dotnet_diagnostic.HRDR004.severity = none   &lt;!-- or per file, in .editorconfig --&gt;
/// </code>
/// </remarks>
public static class EntryPointDiagnostics {
    public const string DiagnosticId = "HRDR004";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor => new(
        id: DiagnosticId,
        title: "More than one Hardened entry point in one assembly",
        messageFormat:
        "'{0}' and '{1}' are both Hardened entry points in this assembly. Each gets its own routing " +
        "table over every handler here, so the two describe identical routes and nothing says which " +
        "one a host runs. To share routes across applications, move the handlers into a " +
        "[WebLibrary] project and reference it. To keep both entry points deliberately, set " +
        "<NoWarn>$(NoWarn);" + DiagnosticId + "</NoWarn>.",
        category: "Hardened.Modules",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports once per compilation, naming the first two entry points in a stable order.
    /// </summary>
    /// <remarks>
    /// One diagnostic rather than one per surplus entry point: the problem is that there is more
    /// than one, not that any particular one is wrong, and a message per module would be N-1 reports
    /// saying the same thing. Ordered by name so the pair named does not change between builds.
    /// </remarks>
    public static void ReportMultipleEntryPoints(
        SourceProductionContext context,
        ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> entryPoints) {
        var diagnostic = For(
            entryPoints.Select(entryPoint => entryPoint.Left.EntryPointType.Name).ToList());

        if (diagnostic != null) {
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// The diagnostic these entry point names warrant, or null when there is at most one.
    /// </summary>
    /// <remarks>
    /// Split from the reporting so the decision is testable. A <c>SourceProductionContext</c> cannot
    /// be constructed outside Roslyn, and driving a second generator from a test project that already
    /// references one is CS0433 on every CSharpAuthor type - both generators compile it in. Over
    /// names alone, this needs neither.
    /// </remarks>
    public static Diagnostic? For(IReadOnlyList<string> entryPointNames) {
        if (entryPointNames.Count < 2) {
            return null;
        }

        var named = entryPointNames.OrderBy(name => name, StringComparer.Ordinal).ToList();

        // Location.None, as everywhere else models are reported from: a syntax location would travel
        // with the model through the incremental caches, which compare models for equality to decide
        // whether to regenerate.
        return Diagnostic.Create(Descriptor, Location.None, named[0], named[1]);
    }
}
