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

        // One declaration per generator that ran, whatever their files are called. Counted here
        // rather than after the names are read, so two generators the path cannot tell apart are
        // still two generators.
        if (marker.DeclaringSyntaxReferences.Length < 2) {
            return;
        }

        // Location.None, as everywhere else this generator reports from: a syntax location would
        // travel through the incremental caches, which compare for equality to decide whether to
        // regenerate. The message carries the names instead.
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor(), Location.None, Join(Emitters(marker))));
    }

    /// <summary>
    /// Which generators declared the marker, read off the paths Roslyn gives generated files.
    /// </summary>
    /// <remarks>
    /// Naming them is most of what makes the message actionable, because the fix is on the
    /// reference that brought one of them. Distinct, because the count was settled above and a
    /// name repeated in the sentence would read as one generator that ran twice.
    /// </remarks>
    private static IReadOnlyList<string> Emitters(INamedTypeSymbol marker) {
        var names = new List<string>();

        foreach (var declaration in marker.DeclaringSyntaxReferences) {
            names.Add(GeneratorName(declaration.SyntaxTree.FilePath));
        }

        return names.Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The generator that wrote a generated file, read off the path Roslyn gives it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generated tree's path is <c>&lt;base&gt;/&lt;generator assembly&gt;/&lt;generator
    /// type&gt;/&lt;hint name&gt;</c>, where the base is the generated-files directory when the
    /// build writes them out and nothing otherwise. So the assembly is the third segment from the
    /// end, never the first. Read as the first, an absolute base - which every project with
    /// <c>EmitCompilerGeneratedFiles</c> on has, the templates included - made both generators
    /// <c>Users</c> or <c>home</c>, and a distinct-count of one reported nothing. That is how the
    /// 0.20 trial referenced two routing generators and got the CS0102s this exists to explain.
    /// </para>
    /// <para>
    /// A path shaped some other way still counts towards the collision; it just cannot be named.
    /// Reporting "two generators" beats reporting nothing.
    /// </para>
    /// </remarks>
    internal static string GeneratorName(string filePath) {
        var segments = filePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 3 ? segments[segments.Length - 3] : "an unnamed generator";
    }

    /// <summary>
    /// The names as a sentence subject: "A and B", or "A, B and C" - or, when every path read
    /// the same, the one name and a count.
    /// </summary>
    private static string Join(IReadOnlyList<string> names) {
        if (names.Count == 1) {
            return "Two copies of " + names[0];
        }

        return names.Count == 2
            ? names[0] + " and " + names[1]
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
    }
}
