using System.Collections.Generic;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Library.SourceGenerator;

/// <summary>
/// Routes with nothing compiling them into a routing table.
/// </summary>
/// <remarks>
/// <para>
/// Reported from this generator because it is the one still referenced when the routing generator
/// is not - a build cannot see a missing analyzer from inside the analyzer that is missing.
/// <see cref="RoutingGeneratorMarker"/> is how a routing generator says it ran.
/// </para>
/// <para>
/// Here rather than beside the marker in the shared source. Five generator assemblies link that
/// folder in and one asks this question, so a decision compiled into the other four is dead weight
/// in each of them - the same reason the library generator's own project file leaves out
/// <c>Models/Request</c>.
/// </para>
/// </remarks>
internal static class MissingRoutingGenerator {

    /// <summary>The attributes a hand-written route is declared with.</summary>
    public static readonly string[] VerbAttributes = {
        "Hardened.Web.Runtime.Attributes.GetAttribute",
        "Hardened.Web.Runtime.Attributes.PostAttribute",
        "Hardened.Web.Runtime.Attributes.PutAttribute",
        "Hardened.Web.Runtime.Attributes.PatchAttribute",
        "Hardened.Web.Runtime.Attributes.DeleteAttribute"
    };

    /// <summary>
    /// <c>HRDR006</c>. <c>HRDR002</c> is about one route; this is about every route in the
    /// assembly at once.
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
            compilation.GetTypeByMetadataName(RoutingGeneratorMarker.TypeName) is not null) {
            return;
        }

        // Location.None, as everywhere else models are reported from: a syntax location would
        // travel through the incremental caches, which compare for equality to decide whether to
        // regenerate. The message carries the type instead.
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor(), Location.None, declaringTypes[0]));
    }
}
