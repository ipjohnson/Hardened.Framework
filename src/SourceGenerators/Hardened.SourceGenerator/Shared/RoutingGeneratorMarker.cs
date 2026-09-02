namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// The type a routing generator declares to say it is running.
/// </summary>
/// <remarks>
/// <para>
/// A module with handlers and no routing generator compiles without a warning into an application
/// that answers 404 to everything. The build cannot see a missing analyzer from inside the analyzer
/// that is missing, so the question is asked from one that is still there: the routing generators
/// emit this as post-initialization output - the one kind of generated source another generator
/// can see - and <c>Hardened.Library.SourceGenerator</c> reports its absence as HRDR006.
/// </para>
/// <para>
/// The same arrangement <c>ValidationGeneratorMarker</c> uses, and for a related reason: a
/// generator cannot observe another's regular output, so a marker is the only channel between two
/// of them.
/// </para>
/// <para>
/// Two constants and no logic, deliberately. This file is linked into five generator assemblies
/// because three of them declare the marker, and only one asks the question - so what the others
/// compile in has to be small enough to carry.
/// </para>
/// </remarks>
public static class RoutingGeneratorMarker {

    /// <summary>The metadata name the check looks for.</summary>
    public const string TypeName = "Hardened.Web.Generated.WebRoutingGeneratorMarker";

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
    public const string Source =
        "namespace Hardened.Web.Generated {\n" +
        "    /// <summary>Declared by a Hardened routing generator so other generators can tell\n" +
        "    /// whether route declarations are being compiled for this compilation.</summary>\n" +
        "    internal static class WebRoutingGeneratorMarker {\n" +
        "    }\n" +
        "}\n";
}
