using Hardened.Amz.SourceGeneration.Testing;
using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// What the generator recomputes when the source changes.
///
/// <para>
/// <see cref="WebLambdaSourceGenerator"/> puts an <c>EntryPointSelector.Comparer</c> on its values
/// provider. That comparer is what lets Roslyn reuse a cached result when an edit cannot affect
/// generated output — get it wrong in one direction and the IDE regenerates on every keystroke, in
/// the other and it serves stale output after a real change.
/// </para>
/// </summary>
public class IncrementalGenerationTests {

    private static IncrementalRunResult Rerun(string first, string second) =>
        GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = first },
            new Dictionary<string, string> { ["Test.cs"] = second },
            [new WebLambdaSourceGenerator()],
            WebGeneratorHarness.Anchors);

    /// <summary>
    /// An edit that cannot reach the emitted file — a method body — is recognised as irrelevant and
    /// nothing is regenerated.
    /// </summary>
    [Fact]
    public void EditingAMethodBodyRegeneratesNothing() {
        var result = Rerun(
            WebGeneratorHarness.Application("""
                    private static int Unrelated() => 1;
                """),
            WebGeneratorHarness.Application("""
                    private static int Unrelated() => 2;
                """));

        Assert.True(result.AllOutputsCached,
            "a method body edit regenerated output: " + string.Join(", ", result.OutputReasons));
    }

    /// <summary>
    /// Adding a <c>Startup</c> method changes what the constructor passes to <c>StartWithWait</c>, so
    /// the application has to be rewritten. Caching here would leave a consumer's startup task unrun
    /// until a full rebuild.
    /// </summary>
    [Fact]
    public void AddingAStartupMethodRewritesTheApplication() {
        var result = Rerun(
            WebGeneratorHarness.Application(),
            WebGeneratorHarness.Application("""
                    private static Task<bool> Startup(IServiceProvider provider) => Task.FromResult(true);
                """));

        Assert.Contains("Startup", result.SecondRun["Application.App.cs"]);
        Assert.DoesNotContain("Startup", result.FirstRun["Application.App.cs"]);
    }

    /// <summary>
    /// Renaming the entry point changes the hint name, so the second run's output is for a different
    /// application entirely.
    /// </summary>
    [Fact]
    public void RenamingTheEntryPointMovesTheEmittedFile() {
        var result = Rerun(
            WebGeneratorHarness.Application(),
            WebGeneratorHarness.NamedApplication("OrderApi"));

        Assert.Contains("Application.App.cs", result.FirstRun.Keys);
        Assert.Contains("OrderApi.App.cs", result.SecondRun.Keys);
        Assert.DoesNotContain("Application.App.cs", result.SecondRun.Keys);
    }

    /// <summary>
    /// Removing the module attribute removes the application. A generator holding the previous run's
    /// output would leave a partial of a class that no longer opts in.
    /// </summary>
    [Fact]
    public void RemovingTheModuleAttributeRemovesTheApplication() {
        var result = Rerun(
            WebGeneratorHarness.Application(),
            WebGeneratorHarness.Application().Replace("[HardenedModule]", ""));

        Assert.NotEmpty(result.FirstRun);
        Assert.Empty(result.SecondRun);
    }

    /// <summary>
    /// Adding an attribute the generator does not read still recomputes: <c>EntryPointSelector</c>
    /// carries every attribute on the entry point into the model, and its comparer compares them. It
    /// is the correct conservative answer — the attribute list is what a future writer would read.
    /// </summary>
    [Fact]
    public void AddingAnAttributeToTheEntryPointRecomputes() {
        var result = Rerun(
            WebGeneratorHarness.Application(),
            WebGeneratorHarness.Application(
                attributes: "[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]"));

        Assert.False(result.AllOutputsCached,
            "an attribute added to the entry point did not reach the model");
    }
}
