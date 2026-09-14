using System.Collections.Immutable;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Gcp.Functions.SourceGenerator;

/// <summary>
/// Writes the Cloud Functions entry type for an application, and reports when the compilation
/// cannot hold one.
/// </summary>
internal static class CloudFunctionGenerator
{
    /// <summary>
    /// Two applications in one assembly, where the Functions Framework will resolve at most one
    /// of them.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error. Both entry types are written and both are valid, so the
    /// compilation is sound; what is not sound is the deployment, because <c>FUNCTION_TARGET</c>
    /// names one type and an unset <c>FUNCTION_TARGET</c> makes the framework refuse to choose -
    /// "Multiple Cloud Function types found". Naming both here is what turns that into a
    /// deploy-script fix rather than a start-up failure read off a log.
    /// </remarks>
    private static readonly DiagnosticDescriptor MultipleEntryPoints = new(
        id: "HRDGF001",
        title: "More than one application in a Cloud Functions assembly",
        messageFormat: "This assembly holds {0} applications, so it has {0} Cloud Functions entry "
            + "types and the deployment has to name one. Set --entry-point, or FUNCTION_TARGET, to "
            + "one of: {1}.",
        category: "Hardened.Gcp",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static void Setup(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider
    )
    {
        // Collected rather than taken one at a time, so the diagnostic can name every target
        // rather than firing once per application with no list to choose from.
        var entryPoints = entryPointProvider.Collect();

        context.RegisterSourceOutput(
            entryPoints,
            SourceGeneratorWrapper.Wrap<ImmutableArray<EntryPointSelector.Model>>(Generate)
        );
    }

    private static void Generate(
        SourceProductionContext context,
        ImmutableArray<EntryPointSelector.Model> entryPoints
    )
    {
        if (entryPoints.Length == 0)
        {
            return;
        }

        if (entryPoints.Length > 1)
        {
            var targets = string.Join(
                ", ",
                entryPoints.Select(CloudFunctionEmitter.TargetName).OrderBy(name => name)
            );

            context.ReportDiagnostic(
                Diagnostic.Create(MultipleEntryPoints, Location.None, entryPoints.Length, targets)
            );
        }

        foreach (var entryPoint in entryPoints)
        {
            context.AddSource(
                entryPoint.EntryPointType.Name + ".CloudFunction.cs",
                GeneratedSource.Header(CloudFunctionEmitter.Emit(entryPoint))
            );
        }
    }
}
