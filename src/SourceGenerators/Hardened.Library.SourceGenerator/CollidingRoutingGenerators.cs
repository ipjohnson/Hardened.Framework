using System;
using System.Collections.Generic;
using System.Linq;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Library.SourceGenerator;

/// <summary>
/// Two routing generators compiling one assembly.
/// </summary>
/// <remarks>
/// <para>
/// A Roslyn generator travels through a <c>ProjectReference</c>, and through a
/// <c>PackageReference</c> that is not a development dependency, unless the reference says
/// <c>PrivateAssets="all"</c>. Reference a code-first module project from a specification-first
/// library, or the library from a code-first host, and both generator sets run over one
/// compilation. Each emits a routing table, a links type and a handler class per operation, so
/// every generated name is declared twice: a screen of CS0102 and CS0111 pointing into
/// <c>obj/**/generated/**</c>, naming the duplicated members and never the reference that brought
/// the second generator.
/// </para>
/// <para>
/// Asked here for the reason <see cref="MissingRoutingGenerator"/> is: this generator is in every
/// Hardened project, and <see cref="RoutingGeneratorMarker"/> is how a routing generator says it
/// ran. Absence and duplication are the same question with two wrong answers.
/// </para>
/// </remarks>
internal static class CollidingRoutingGenerators {

    /// <summary>
    /// <c>HRDR008</c>. <c>HRDR006</c> is no routing generator; this is more than one.
    /// </summary>
    public const string DiagnosticId = "HRDR008";

    /// <summary>
    /// Built per call rather than held in a static field: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "More than one routing generator is compiling this assembly",
        messageFormat:
        "{0} are both compiling this project's routes, so every generated name - the routing " +
        "table, the links type and a class per handler - is declared twice, as CS0102 and CS0111 " +
        "in obj/**/generated/**. A generator reaches this project through a ProjectReference, or " +
        "a PackageReference that is not a development dependency, unless the reference says " +
        "PrivateAssets=\"all\". Add it to the one that brought the second generator.",
        category: "Hardened.Routing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Reports the collision, if there is one.</summary>
    public static void Report(SourceProductionContext context, Compilation compilation) {
        if (compilation.GetTypeByMetadataName(RoutingGeneratorMarker.TypeName)
            is not { } marker) {
            return;
        }

        var generators = Emitters(marker);

        if (generators.Count < 2) {
            return;
        }

        // Location.None, as everywhere else this generator reports from: a syntax location would
        // travel through the incremental caches, which compare for equality to decide whether to
        // regenerate. The message carries the names instead.
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor(), Location.None, Join(generators)));
    }

    /// <summary>
    /// Which generators declared the marker, read off the paths Roslyn gives generated files.
    /// </summary>
    /// <remarks>
    /// A generated file's path is the generator's assembly, then its type, then the hint name.
    /// Both routing generators use the same hint name, so the assembly is what tells them apart -
    /// and naming them is most of what makes the message actionable, because the fix is on the
    /// reference that brought one of them.
    /// </remarks>
    private static IReadOnlyList<string> Emitters(INamedTypeSymbol marker) {
        var names = new List<string>();

        foreach (var declaration in marker.DeclaringSyntaxReferences) {
            var assembly = declaration.SyntaxTree.FilePath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            // A path Roslyn shaped differently still counts towards the collision; it just cannot
            // be named. Reporting "two generators" beats reporting nothing.
            names.Add(string.IsNullOrEmpty(assembly) ? "an unnamed generator" : assembly!);
        }

        return names.Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The names as a sentence subject: "A and B", or "A, B and C".</summary>
    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 2
            ? names[0] + " and " + names[1]
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
}
