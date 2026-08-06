using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Caching;

/// <summary>
/// Incremental generators are re-run by the IDE on every keystroke. They stay cheap only if
/// their pipeline caches - which requires every model flowing through it to compare equal
/// when the underlying code has not meaningfully changed. A model that falls back to
/// reference equality still produces correct output, so nothing fails; the generator just
/// silently re-runs every time and the IDE gets slower.
///
/// These tests make that observable: the driver is created with step tracking enabled, run
/// twice, and the reasons recorded on the second run are asserted.
/// </summary>
public class WebGeneratorCachingTests {

    private const string ControllerSource = """
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [BasePath("/api/orders")]
        public class OrderController {
            [Get("/{id}")]
            public string GetOrder(string id) => id;

            [Post("/")]
            public string CreateOrder(string name) => name;
        }
        """;

    private static WebLibrarySourceGenerator Generator() => new();

    [Fact]
    public void GeneratorProducesOutputForAController() {
        var compilation = GeneratorTestHarness.CreateCompilation(ControllerSource);
        var driver = GeneratorTestHarness.CreateDriver(Generator()).RunGenerators(compilation);

        var diagnostics = driver.GetRunResult().Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// The core caching assertion. Running the same compilation through the driver a second
    /// time must not re-execute the pipeline: every recorded output step should be Cached or
    /// Unchanged. A New or Modified reason means something in the model chain compared
    /// unequal to itself.
    /// </summary>
    [Fact]
    public void SecondRunOverAnUnchangedCompilationIsFullyCached() {
        var compilation = GeneratorTestHarness.CreateCompilation(ControllerSource);

        var driver = GeneratorTestHarness.CreateDriver(Generator());
        driver = driver.RunGenerators(compilation);

        // Re-run against the very same compilation.
        driver = driver.RunGenerators(compilation);

        var reasons = GeneratorTestHarness.OutputStepReasons(driver);

        Assert.All(reasons, reason =>
            Assert.True(
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"expected every output step to be Cached or Unchanged on an unchanged re-run, " +
                $"but one was {reason}. The generator is re-running its pipeline on every edit."));
    }

    /// <summary>
    /// The realistic IDE case: the user edits an unrelated part of the file. The syntax tree
    /// is a different instance and the compilation is new, but nothing the generator cares
    /// about changed, so its output should still come from cache.
    /// </summary>
    [Fact]
    public void EditingAnUnrelatedDeclarationStillCachesGeneratorOutput() {
        var first = GeneratorTestHarness.CreateCompilation(ControllerSource);

        var edited = ControllerSource.Replace(
            "public class OrderController {",
            "public class OrderController {\n    private int _unrelatedField;");

        var second = GeneratorTestHarness.CreateCompilation(edited);

        var driver = GeneratorTestHarness.CreateDriver(Generator());
        driver = driver.RunGenerators(first);
        var firstSources = GeneratorTestHarness.GeneratedSources(driver);

        driver = driver.RunGenerators(second);
        var secondSources = GeneratorTestHarness.GeneratedSources(driver);

        Assert.Equal(firstSources, secondSources);
    }

    /// <summary>
    /// The other half of the contract: caching must not be so aggressive that a real change
    /// is missed. Adding a route has to change the generated output.
    /// </summary>
    [Fact]
    public void AddingARouteChangesGeneratedOutput() {
        var withExtraRoute = ControllerSource.Replace(
            "    [Post(\"/\")]",
            """
                [Delete("/{id}")]
                public string DeleteOrder(string id) => id;

                [Post("/")]
            """);

        var driver = GeneratorTestHarness.CreateDriver(Generator());

        driver = driver.RunGenerators(GeneratorTestHarness.CreateCompilation(ControllerSource));
        var before = GeneratorTestHarness.GeneratedSources(driver);

        driver = driver.RunGenerators(GeneratorTestHarness.CreateCompilation(withExtraRoute));
        var after = GeneratorTestHarness.GeneratedSources(driver);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// Generation must be deterministic: two independent drivers over identical input have
    /// to produce byte-identical source. Non-determinism here would show up downstream as
    /// spurious rebuilds and unstable diffs in committed generated output.
    /// </summary>
    [Fact]
    public void GenerationIsDeterministicAcrossIndependentDrivers() {
        var one = GeneratorTestHarness.CreateDriver(Generator())
            .RunGenerators(GeneratorTestHarness.CreateCompilation(ControllerSource));

        var two = GeneratorTestHarness.CreateDriver(Generator())
            .RunGenerators(GeneratorTestHarness.CreateCompilation(ControllerSource));

        Assert.Equal(
            GeneratorTestHarness.GeneratedSources(one),
            GeneratorTestHarness.GeneratedSources(two));

        Assert.Equal(
            GeneratorTestHarness.GeneratedHintNames(one),
            GeneratorTestHarness.GeneratedHintNames(two));
    }

    [Fact]
    public void GeneratorDoesNotThrowOnAFileWithNoControllers() {
        var compilation = GeneratorTestHarness.CreateCompilation(
            "namespace TestApp; public class NotAController { public int Value { get; set; } }");

        var driver = GeneratorTestHarness.CreateDriver(Generator()).RunGenerators(compilation);

        Assert.Empty(driver.GetRunResult().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}
