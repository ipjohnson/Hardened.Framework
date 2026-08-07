using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Hardened.Web.SourceGenerator.Tests.Caching;

/// <summary>
/// Runs an <see cref="IIncrementalGenerator"/> through a real <see cref="GeneratorDriver"/>
/// so that both its output and its incremental caching behaviour can be asserted.
/// </summary>
internal static class GeneratorTestHarness {

    /// <summary>
    /// Reference set wide enough for a Hardened controller to compile: the framework
    /// assemblies plus everything currently loaded, which covers the BCL.
    /// </summary>
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToImmutableArray());

    public static CSharpCompilation CreateCompilation(string source, string assemblyName = "GeneratorCacheTests") =>
        CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// Creates a driver with step tracking enabled. Tracking is what makes
    /// <see cref="GeneratorRunResult.TrackedOutputSteps"/> populated, and therefore what
    /// makes cache behaviour observable at all.
    /// </summary>
    public static GeneratorDriver CreateDriver(IIncrementalGenerator generator) =>
        CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

    /// <summary>
    /// Every reason recorded against the driver's output steps for a run.
    /// </summary>
    public static IReadOnlyList<IncrementalStepRunReason> OutputStepReasons(GeneratorDriver driver) =>
        driver.GetRunResult().Results
            .SelectMany(result => result.TrackedOutputSteps)
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToList();

    public static IReadOnlyList<string> GeneratedSources(GeneratorDriver driver) =>
        driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .OrderBy(source => source.HintName, StringComparer.Ordinal)
            .Select(source => source.SourceText.ToString())
            .ToList();

    public static IReadOnlyList<string> GeneratedHintNames(GeneratorDriver driver) =>
        driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.HintName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
}
