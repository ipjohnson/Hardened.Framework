using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Hardened.SourceGeneration.Testing;

/// <summary>
/// Runs a Hardened source generator over an in-memory compilation so tests can assert on the code
/// it produces without going through a real build.
///
/// <para>
/// The assertion that matters is <see cref="GeneratorResult.AssertNoErrors"/>, which checks the
/// compilation <em>including the generated trees</em>. Until 2026-08-11 the generator suites checked
/// only <c>driver.GetRunResult().Diagnostics</c> — what the generator <em>said</em>, not whether what
/// it <em>wrote</em> compiles. Three defects that emitted uncompilable C# passed every generator test
/// and were caught by integration tests instead, after shipping.
/// </para>
///
/// <para>
/// Adapted from <c>DependencyModules.Tests/Infrastructure/GeneratorTestHarness.cs</c>. The two
/// repositories deliberately keep separate copies; a fix worth having in one is usually worth
/// porting to the other.
/// </para>
/// </summary>
public static class GeneratorTestHarness {

    /// <summary>
    /// Compiles <paramref name="sources"/>, runs <paramref name="generators"/>, and returns
    /// everything they emitted.
    /// </summary>
    /// <param name="sources">
    /// Source files keyed by file name. Names matter: generators compare a file's location against
    /// <c>ProjectDir</c>, so the sources are rooted under it.
    /// </param>
    /// <param name="generators">
    /// The generators to run. Required — Hardened ships ten, and which one is under test is never
    /// implicit.
    /// </param>
    /// <param name="referenceAnchors">
    /// One type from each assembly the source under test binds against. A type rather than a name
    /// because <c>typeof(T)</c> forces the assembly to load, which is the point.
    /// </param>
    /// <param name="additionalTexts">
    /// <c>AdditionalFiles</c> content keyed by file name — OpenAPI specifications and templates
    /// reach their generators this way, so a test for either needs them.
    /// </param>
    /// <param name="buildProperties">
    /// MSBuild properties visible to the generator, without the <c>build_property.</c> prefix.
    /// </param>
    public static GeneratorResult Run(
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyList<IIncrementalGenerator> generators,
        IReadOnlyList<Type>? referenceAnchors = null,
        IReadOnlyDictionary<string, string>? additionalTexts = null,
        IReadOnlyDictionary<string, string>? buildProperties = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        string assemblyName = "GeneratorTestAssembly",
        IReadOnlyList<MetadataReference>? additionalReferences = null) {

        var projectDir = ResolveProjectDir(buildProperties);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        var syntaxTrees = sources
            .Select(pair => CSharpSyntaxTree.ParseText(
                pair.Value,
                parseOptions,
                path: Path.Combine(projectDir, pair.Key)))
            .ToArray();

        var references = GeneratorReferences.For(referenceAnchors ?? Array.Empty<Type>());

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            additionalReferences == null ? references : references.Concat(additionalReferences),
            new CSharpCompilationOptions(outputKind, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            generators.Select(generator => generator.AsSourceGenerator()).ToArray(),
            additionalTexts: (additionalTexts ?? new Dictionary<string, string>())
                .Select(pair => (AdditionalText)new TestAdditionalText(
                    Path.Combine(projectDir, pair.Key), pair.Value))
                .ToArray(),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties, projectDir),
            parseOptions: parseOptions);

        var updated = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDiagnostics);

        var runResult = updated.GetRunResult();

        // Hint names are unique within a generator but not across them, so a run with more than one
        // generator can produce the same name twice. Two generators emitting one type's partial
        // twice is a real defect, so it is recorded and asserted on rather than throwing behind a
        // dictionary error.
        var emitted = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generated => (generated.HintName, Source: generated.SourceText.ToString()))
            .ToArray();

        var duplicateHintNames = emitted
            .GroupBy(generated => generated.HintName)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        var generatedSources = emitted
            .GroupBy(generated => generated.HintName)
            .ToDictionary(group => group.Key, group => group.First().Source);

        // A generator that catches its own exceptions and has nowhere to log them produces nothing
        // and reports success. Surface them so a crash fails loudly.
        var exceptions = runResult.Results
            .Select(result => result.Exception)
            .Where(exception => exception != null)
            .ToArray();

        return new GeneratorResult(
            generatedSources,
            generatorDiagnostics,
            outputCompilation.GetDiagnostics(),
            outputCompilation,
            exceptions!,
            duplicateHintNames);
    }

    /// <summary>Convenience overload for the common single-file, single-generator case.</summary>
    public static GeneratorResult Run(
        string source,
        IIncrementalGenerator generator,
        IReadOnlyList<Type>? referenceAnchors = null,
        IReadOnlyDictionary<string, string>? additionalTexts = null,
        IReadOnlyDictionary<string, string>? buildProperties = null) =>
        Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            new[] { generator },
            referenceAnchors,
            additionalTexts,
            buildProperties);

    /// <summary>
    /// Runs the generator over <paramref name="first"/>, then re-runs the same driver over
    /// <paramref name="second"/>, reporting why each output was or was not recomputed.
    ///
    /// <para>
    /// This is how the model comparers earn their keep: when an edit cannot affect generated output,
    /// Roslyn should reuse the cached result rather than regenerate. Getting it wrong makes the IDE
    /// recompute on every keystroke, or serve stale output after a real change.
    /// </para>
    /// </summary>
    public static IncrementalRunResult RunIncremental(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second,
        IReadOnlyList<IIncrementalGenerator> generators,
        IReadOnlyList<Type>? referenceAnchors = null,
        IReadOnlyDictionary<string, string>? additionalTexts = null,
        IReadOnlyDictionary<string, string>? buildProperties = null) {

        var projectDir = ResolveProjectDir(buildProperties);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var references = GeneratorReferences.For(referenceAnchors ?? Array.Empty<Type>());

        Compilation Compile(IReadOnlyDictionary<string, string> sources) =>
            CSharpCompilation.Create(
                "GeneratorTestAssembly",
                sources.Select(pair => CSharpSyntaxTree.ParseText(
                    pair.Value, parseOptions, path: Path.Combine(projectDir, pair.Key))),
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(generator => generator.AsSourceGenerator()).ToArray(),
            additionalTexts: (additionalTexts ?? new Dictionary<string, string>())
                .Select(pair => (AdditionalText)new TestAdditionalText(
                    Path.Combine(projectDir, pair.Key), pair.Value))
                .ToArray(),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties, projectDir),
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(Compile(first));
        var firstOutputs = Outputs(driver.GetRunResult());

        driver = driver.RunGenerators(Compile(second));
        var secondRun = driver.GetRunResult();

        var reasons = secondRun.Results
            .SelectMany(result => result.TrackedOutputSteps)
            .SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToArray();

        return new IncrementalRunResult(firstOutputs, Outputs(secondRun), reasons);
    }

    /// <summary>
    /// Compiles a standalone library and returns a reference to it, plus the loaded assembly.
    /// </summary>
    /// <remarks>
    /// The only honest way to test scanning a referenced assembly: the types have to live in real
    /// metadata with no syntax tree in the consuming compilation. Loading it as well lets a
    /// behavioural test resolve the services the generated code registers.
    /// </remarks>
    public static (MetadataReference Reference, Assembly Assembly) CompileLibrary(
        string source,
        string assemblyName,
        IReadOnlyList<Type>? referenceAnchors = null) {

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorReferences.For(referenceAnchors ?? Array.Empty<Type>()),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();

        var result = compilation.Emit(stream);

        Xunit.Assert.True(result.Success,
            "The test library did not compile: " + string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var bytes = stream.ToArray();
        var assembly = Assembly.Load(bytes);

        // Assembly.Load(byte[]) puts it in the default context but does not make it discoverable by
        // name, so generated code referencing it fails to bind at run time. The resolver closes that.
        lock (LoadedLibraries) {
            LoadedLibraries[assemblyName] = assembly;

            if (!_resolverHooked) {
                AssemblyLoadContext.Default.Resolving += (_, name) => {
                    lock (LoadedLibraries) {
                        return name.Name != null && LoadedLibraries.TryGetValue(name.Name, out var found)
                            ? found
                            : null;
                    }
                };

                _resolverHooked = true;
            }
        }

        return (MetadataReference.CreateFromImage(bytes), assembly);
    }

    internal static string DefaultProjectDir { get; } =
        Path.Combine(Path.GetTempPath(), "HardenedGeneratorTest") + Path.DirectorySeparatorChar;

    private static readonly Dictionary<string, Assembly> LoadedLibraries = new();

    private static bool _resolverHooked;

    private static IReadOnlyDictionary<string, string> Outputs(GeneratorDriverRunResult runResult) =>
        runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .GroupBy(generated => generated.HintName)
            .ToDictionary(group => group.Key, group => group.First().SourceText.ToString());

    private static string ResolveProjectDir(IReadOnlyDictionary<string, string>? buildProperties) =>
        buildProperties != null && buildProperties.TryGetValue("ProjectDir", out var configured)
            ? configured
            : DefaultProjectDir;
}
