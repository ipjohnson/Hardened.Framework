using Hardened.Amz.SourceGeneration.Testing;
using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// What the generator recomputes when the source changes.
///
/// <para>
/// <see cref="LambdaFunctionSourceGenerator"/> puts an <c>EntryPointSelector.Comparer</c> on its
/// values provider. That comparer is what lets Roslyn reuse a cached result when an edit cannot
/// affect generated output — get it wrong in one direction and the IDE regenerates on every
/// keystroke, in the other and it serves stale output after a real change.
/// </para>
/// </summary>
public class IncrementalGenerationTests {

    private static IncrementalRunResult Rerun(string first, string second) =>
        GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = first },
            new Dictionary<string, string> { ["Test.cs"] = second },
            [new LambdaFunctionSourceGenerator()],
            FunctionGeneratorHarness.Anchors);

    /// <summary>
    /// An edit that cannot reach the emitted files — a method body — is recognised as irrelevant and
    /// nothing is regenerated.
    /// </summary>
    [Fact]
    public void EditingAMethodBodyRegeneratesNothing() {
        var result = Rerun(
            FunctionGeneratorHarness.Application("""
                    private static int Unrelated() => 1;
                """),
            FunctionGeneratorHarness.Application("""
                    private static int Unrelated() => 2;
                """));

        Assert.True(result.AllOutputsCached,
            "a method body edit regenerated output: " + string.Join(", ", result.OutputReasons));
    }

    /// <summary>
    /// Adding a <c>Startup</c> method changes what the constructor passes to <c>StartWithWait</c>, so
    /// the application has to be rewritten. Caching here would leave a consumer's startup task
    /// unrun until a full rebuild.
    /// </summary>
    [Fact]
    public void AddingAStartupMethodRewritesTheApplication() {
        var result = Rerun(
            FunctionGeneratorHarness.Application(),
            FunctionGeneratorHarness.Application("""
                    private static Task<bool> Startup(IServiceProvider provider) => Task.FromResult(true);
                """));

        var application = result.SecondRun["Application.LambdaApplication.cs"];

        Assert.Contains("Startup", application);
        Assert.DoesNotContain("Startup", result.FirstRun["Application.LambdaApplication.cs"]);
    }

    /// <summary>
    /// Renaming the entry point changes every hint name and the type the registry is keyed on, so the
    /// second run's outputs are for a different application entirely.
    /// </summary>
    [Fact]
    public void RenamingTheEntryPointMovesEveryEmittedFile() {
        var result = Rerun(
            FunctionGeneratorHarness.Application(),
            FunctionGeneratorHarness.NamedApplication("OrderProcessor"));

        Assert.Contains("Application.LambdaApplication.cs", result.FirstRun.Keys);
        Assert.Contains("OrderProcessor.LambdaApplication.cs", result.SecondRun.Keys);
        Assert.DoesNotContain("Application.LambdaApplication.cs", result.SecondRun.Keys);
    }

    /// <summary>
    /// Removing the module attribute removes the application. A generator holding the previous run's
    /// output would leave a partial of a class that no longer opts in.
    /// </summary>
    [Fact]
    public void RemovingTheModuleAttributeRemovesTheApplication() {
        var result = Rerun(
            FunctionGeneratorHarness.Application(),
            FunctionGeneratorHarness.Application().Replace("[HardenedModule]", ""));

        Assert.NotEmpty(result.FirstRun);
        Assert.Empty(result.SecondRun);
    }
}
