using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// Whether anything in this compilation is turning route declarations into a routing table.
/// </summary>
/// <remarks>
/// <para>
/// A module with handlers and no routing generator compiles without a warning into an application
/// that answers 404 to everything. The build cannot see a missing analyzer from inside the analyzer
/// that is missing, so the question is asked from one that is still there: the routing generators
/// declare <see cref="MarkerTypeName"/> as post-initialization output - the one kind of generated
/// source another generator can see - and the library generator reports its absence.
/// </para>
/// <para>
/// The same arrangement <c>ValidationGeneratorMarker</c> uses, and for a related reason: a
/// generator cannot observe another's regular output, so a marker is the only channel between two
/// of them.
/// </para>
/// </remarks>
public static class RoutingGeneratorPresence {

    /// <summary>The type a routing generator declares to say it is running.</summary>
    public const string MarkerTypeName = "Hardened.Web.Generated.WebRoutingGeneratorMarker";

    /// <summary>
    /// Deliberately plain C#: block-scoped namespace, ordinary class body. This lands in whatever
    /// project references the generator, and a marker that needs a language version to compile
    /// would fail the builds it exists to keep working.
    /// </summary>
    /// <remarks>
    /// Written without the generated-file header so <c>GeneratedSource.Header</c> adds it - that
    /// method leaves a source that already opens with the marker alone, which would mean this file
    /// carrying the marker and neither the nullable context nor the CS1591 pragma that travel with
    /// it.
    /// </remarks>
    public const string MarkerSource =
        "namespace Hardened.Web.Generated {\n" +
        "    /// <summary>Declared by a Hardened routing generator so other generators can tell\n" +
        "    /// whether route declarations are being compiled for this compilation.</summary>\n" +
        "    internal static class WebRoutingGeneratorMarker {\n" +
        "    }\n" +
        "}\n";

    /// <summary>The attributes a hand-written route is declared with.</summary>
    public static readonly string[] VerbAttributes = {
        "Hardened.Web.Runtime.Attributes.GetAttribute",
        "Hardened.Web.Runtime.Attributes.PostAttribute",
        "Hardened.Web.Runtime.Attributes.PutAttribute",
        "Hardened.Web.Runtime.Attributes.PatchAttribute",
        "Hardened.Web.Runtime.Attributes.DeleteAttribute"
    };

    /// <summary>
    /// <c>HRDR006</c>. <c>HRDR002</c> and <c>HRDR005</c> are about one route; this is about every
    /// route in the assembly at once.
    /// </summary>
    public const string DiagnosticId = "HRDR006";

    /// <summary>
    /// Built per call rather than held in a static field: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "No routing generator is compiling this assembly's routes",
        messageFormat:
        "'{0}' declares routes and nothing in this project turns them into a routing table, so " +
        "every one of them answers 404 at run time. Reference Hardened.Web.SourceGenerator as an " +
        "analyzer, or drop the route attributes if this assembly is not meant to serve them.",
        category: "Hardened.Routing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports the absence, having been handed the route declarations the compilation carries.
    /// </summary>
    /// <param name="declaringTypes">
    /// The types carrying route attributes, in the order they were found. One report for the first
    /// of them rather than one per route: there is a single thing to fix, and an assembly with
    /// forty routes would otherwise produce forty copies of it.
    /// </param>
    public static void Report(
        SourceProductionContext context, Compilation compilation,
        IReadOnlyList<string> declaringTypes) {
        if (declaringTypes.Count == 0 ||
            compilation.GetTypeByMetadataName(MarkerTypeName) is not null) {
            return;
        }

        // Location.None, as everywhere else models are reported from: a syntax location would
        // travel through the incremental caches, which compare for equality to decide whether to
        // regenerate. The message carries the type instead.
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor(), Location.None, declaringTypes[0]));
    }
}
