using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using Xunit;

namespace Hardened.SourceGeneration.Testing.Tests;

/// <summary>
/// A harness that cannot fail is worse than no harness, because it reads as coverage.
///
/// <para>
/// These tests drive deliberately broken generators through <see cref="GeneratorTestHarness"/> and
/// assert it notices. Without them, a refactor that quietly stopped checking the output compilation
/// would leave every generator suite green — which is exactly the state the suites were in before
/// 2026-08-11.
/// </para>
/// </summary>
public class HarnessCatchesBrokenGeneratorsTests {

    [Fact]
    public void OutputThatDoesNotCompileFailsTheAssertion() {
        var result = GeneratorTestHarness.Run(
            "namespace TestApp; public class Marker { }",
            new EmittingGenerator("Broken.g.cs", "namespace TestApp { public class Broken { void M() { var x = ; } } }"));

        var failure = Assert.ThrowsAny<Exception>(() => result.AssertNoErrors());

        // The compiler's own error id for a missing expression.
        Assert.Contains("CS1525", failure.Message);
    }

    /// <summary>
    /// The whole point of the failure message: a compiler error with no source attached cannot be
    /// diagnosed, so the offending file is printed with line numbers and the bad line marked.
    /// </summary>
    [Fact]
    public void TheFailureMessageContainsTheGeneratedSource() {
        var result = GeneratorTestHarness.Run(
            "namespace TestApp; public class Marker { }",
            new EmittingGenerator("Broken.g.cs", "namespace TestApp { public class Broken { void M() { var x = ; } } }"));

        var failure = Assert.ThrowsAny<Exception>(() => result.AssertNoErrors());

        Assert.Contains("Broken.g.cs", failure.Message);
        Assert.Contains("var x = ;", failure.Message);
        Assert.Contains(">", failure.Message);
    }

    /// <summary>
    /// A generator that throws produces nothing and reports success. Roslyn hands the exception back
    /// on the run result rather than rethrowing, so a harness that ignores it sees an empty run and
    /// passes.
    /// </summary>
    [Fact]
    public void AGeneratorThatThrowsFailsTheAssertion() {
        var result = GeneratorTestHarness.Run(
            "namespace TestApp; public class Marker { }",
            new ThrowingGenerator());

        var failure = Assert.ThrowsAny<Exception>(() => result.AssertNoErrors());

        Assert.Contains("The generator threw", failure.Message);
    }

    /// <summary>
    /// Two generators emitting the same hint name means one output silently replaced another.
    /// </summary>
    [Fact]
    public void DuplicateHintNamesFailTheAssertion() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = "namespace TestApp; public class Marker { }" },
            new IIncrementalGenerator[] {
                new EmittingGenerator("Same.g.cs", "namespace TestApp { public class A { } }"),
                new EmittingGenerator("Same.g.cs", "namespace TestApp { public class B { } }")
            });

        var failure = Assert.ThrowsAny<Exception>(() => result.AssertNoErrors());

        Assert.Contains("same hint name", failure.Message);
    }

    [Fact]
    public void OutputThatCompilesPassesTheAssertion() {
        GeneratorTestHarness.Run(
                "namespace TestApp; public class Marker { }",
                new EmittingGenerator("Fine.g.cs", "namespace TestApp { public class Fine { } }"))
            .AssertNoErrors();
    }

    /// <summary>
    /// AdditionalFiles is how OpenAPI specifications and templates reach their generators, so a
    /// harness that drops them cannot test either.
    /// </summary>
    [Fact]
    public void AdditionalFilesReachTheGenerator() {
        var result = GeneratorTestHarness.Run(
            "namespace TestApp; public class Marker { }",
            new AdditionalTextEchoGenerator(),
            additionalTexts: new Dictionary<string, string> { ["spec.yaml"] = "openapi: 3.0.0" });

        result.AssertNoErrors();

        Assert.Contains("spec.yaml", result.SourceContaining("Echo"));
        Assert.Contains("openapi: 3.0.0", result.SourceContaining("Echo"));
    }

    /// <summary>
    /// Build properties reach the generator, and the documented defaults apply when a test supplies
    /// none. <c>RootNamespace</c> is read by every Hardened generator.
    /// </summary>
    [Fact]
    public void BuildPropertiesReachTheGenerator() {
        var withDefault = GeneratorTestHarness.Run(
            "namespace TestApp; public class Marker { }",
            new BuildPropertyEchoGenerator("RootNamespace"));

        Assert.Contains("TestNamespace", withDefault.SourceContaining("Echo"));

        var overridden = GeneratorTestHarness.Run(
            "namespace TestApp; public class Marker { }",
            new BuildPropertyEchoGenerator("RootNamespace"),
            buildProperties: new Dictionary<string, string> { ["RootNamespace"] = "Contoso.Api" });

        Assert.Contains("Contoso.Api", overridden.SourceContaining("Echo"));
    }

    /// <summary>
    /// Reference anchors have to pull in what the anchor assembly itself references, or source that
    /// legitimately compiles is reported as a generator defect.
    /// </summary>
    [Fact]
    public void ReferenceAnchorsResolveTransitively() {
        GeneratorTestHarness.Run(
                """
                using Microsoft.CodeAnalysis;

                namespace TestApp;

                public class UsesATransitiveReference {
                    public DiagnosticSeverity Severity => DiagnosticSeverity.Error;
                }
                """,
                new EmittingGenerator("Fine.g.cs", "namespace TestApp { public class Fine { } }"),
                referenceAnchors: [typeof(GeneratorTestHarness)])
            .AssertNoErrors();
    }

    /// <summary>
    /// Every diagnostic in this workspace is reported at <see cref="Location.None"/> — deliberately,
    /// because a syntax location would travel with the model through the incremental caches. That
    /// gives the diagnostic a null <c>Path</c>, and <c>Describe</c> used to call
    /// <c>span.Path.EndsWith(...)</c> on it, so the harness threw a <see cref="NullReferenceException"/>
    /// from inside its own failure formatting.
    /// </summary>
    /// <remarks>
    /// The case that broke was the case that mattered: a routing diagnostic firing at error severity
    /// made every test in that suite fail with an NRE pointing into the harness, hiding both the
    /// diagnostic and whatever the generated code did wrong.
    /// </remarks>
    [Fact]
    public void AnErrorWithNoLocationIsDescribedRatherThanThrowing() {
        var result = GeneratorTestHarness.Run(
            "namespace TestApp; public class Marker { }",
            new DiagnosticReportingGenerator());

        var failure = Assert.ThrowsAny<Exception>(() => result.AssertNoErrors());

        Assert.IsNotType<NullReferenceException>(failure);
        Assert.Contains("HRDTEST", failure.Message);
        Assert.Contains("no location", failure.Message);
    }

    private sealed class EmittingGenerator(string hintName, string source) : IIncrementalGenerator {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterPostInitializationOutput(
                ctx => ctx.AddSource(hintName, SourceText.From(source, Encoding.UTF8)));
    }

    /// <summary>Reports an error at <see cref="Location.None"/>, as every generator here does.</summary>
    private sealed class DiagnosticReportingGenerator : IIncrementalGenerator {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(
                context.CompilationProvider,
                (production, _) => production.ReportDiagnostic(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            id: "HRDTEST",
                            title: "Deliberate",
                            messageFormat: "Reported with no location, as the real ones are.",
                            category: "Hardened.Testing",
                            defaultSeverity: DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None)));
    }

    private sealed class ThrowingGenerator : IIncrementalGenerator {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(
                context.CompilationProvider,
                (_, _) => throw new InvalidOperationException("deliberate"));
    }

    private sealed class AdditionalTextEchoGenerator : IIncrementalGenerator {
        public void Initialize(IncrementalGeneratorInitializationContext context) {
            var texts = context.AdditionalTextsProvider
                .Select((text, token) => (Path.GetFileName(text.Path), text.GetText(token)?.ToString() ?? ""))
                .Collect();

            context.RegisterSourceOutput(texts, (ctx, all) => {
                var body = string.Join(
                    Environment.NewLine,
                    all.Select(pair => $"// {pair.Item1}: {pair.Item2}"));

                ctx.AddSource("Echo.g.cs", SourceText.From(
                    $"{body}{Environment.NewLine}namespace TestApp {{ public class Echo {{ }} }}",
                    Encoding.UTF8));
            });
        }
    }

    private sealed class BuildPropertyEchoGenerator(string property) : IIncrementalGenerator {
        public void Initialize(IncrementalGeneratorInitializationContext context) {
            var value = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
                options.GlobalOptions.TryGetValue("build_property." + property, out var found)
                    ? found
                    : "(unset)");

            context.RegisterSourceOutput(value, (ctx, found) =>
                ctx.AddSource("Echo.g.cs", SourceText.From(
                    $"// {property} = {found}{Environment.NewLine}namespace TestApp {{ public class Echo {{ }} }}",
                    Encoding.UTF8)));
        }
    }
}
