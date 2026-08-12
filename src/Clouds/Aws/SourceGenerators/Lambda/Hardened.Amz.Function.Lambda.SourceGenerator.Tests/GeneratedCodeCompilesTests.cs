using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// The assertion that matters: what <see cref="LambdaFunctionSourceGenerator"/> writes is compiled
/// together with the source it was given.
///
/// <para>
/// A test asserting on an emitted string proves the generator produced the characters expected; it
/// does not prove a consumer can build. Three defects in the sibling web generator emitted
/// uncompilable C#, passed every string-matching test, and were caught by integration tests after
/// shipping — see <c>docs/testing-conventions.md</c> §1.
/// </para>
///
/// <para>
/// Every case here also asserts the generator did not crash. That is a separate check because the
/// framework's <c>SourceGeneratorWrapper</c> catches a writer's exception and reports it as a
/// <em>Warning</em>, so a crashed generator emits nothing and still compiles clean.
/// </para>
/// </summary>
public class GeneratedCodeCompilesTests {

    /// <summary>
    /// The plainest application a consumer can write — the shape <c>src/SqsTest</c> uses.
    /// </summary>
    [Fact]
    public void AnApplicationWithNothingButTheModuleAttributeCompiles() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application());

        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.Contains("Application.LambdaHandlerPackage.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// A <c>Startup</c> method is handed to <c>StartWithWait</c> as a method group. Miss it and the
    /// application starts without ever running the consumer's startup task.
    /// </summary>
    [Fact]
    public void AStartupMethodIsWaitedOnBeforeTheHandlerIsWired() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                private static Task<bool> Startup(IServiceProvider provider) => Task.FromResult(true);
            """));

        var application = result.SourceContaining("LambdaApplication");

        FunctionGeneratorHarness.AssertEmits(application, "StartWithWait(RootServiceProvider, Startup, 15)");

        var startup = application.IndexOf("StartWithWait", StringComparison.Ordinal);
        var wiring = application.IndexOf("ILambdaInvokeFilterProvider", StringComparison.Ordinal);

        Assert.True(startup < wiring,
            "the invoke filter is resolved before startup has run, so a handler can be built from a " +
            "half-initialised container");
    }

    [Fact]
    public void AnApplicationWithoutAStartupMethodStartsWithNoStartupTask() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application());

        FunctionGeneratorHarness.AssertEmits(
            result.SourceContaining("LambdaApplication"), "StartWithWait(RootServiceProvider, null, 15)");
    }

    /// <summary>
    /// The three logging shapes, which pick different arguments for <c>CreateServiceProvider</c>.
    /// The one-parameter <c>ConfigureLogging</c> goes through as a method group; the two-parameter
    /// form cannot, so the environment is closed over in a lambda first; neither present means no
    /// logging action at all.
    /// </summary>
    [Theory]
    [InlineData("", "CreateServiceProvider(environment, overrideDependencies, null, RegisterInitDi)")]
    [InlineData("    private static void ConfigureLogging(ILoggingBuilder builder) { }",
        "CreateServiceProvider(environment, overrideDependencies, ConfigureLogging, RegisterInitDi)")]
    [InlineData("    private static void ConfigureLogging(IHardenedEnvironment environment, ILoggingBuilder builder) { }",
        "CreateServiceProvider(environment, overrideDependencies, loggingBuilderAction, RegisterInitDi)")]
    public void TheLoggingMethodTheEntryPointDeclaresDecidesWhatIsPassedToTheProviderFactory(
        string member, string expected) {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application(member));

        FunctionGeneratorHarness.AssertEmits(result.SourceContaining("LambdaApplication"), expected);
    }

    /// <summary>
    /// Public settable properties on the entry point are read into the model by
    /// <c>EntryPointSelector</c> before any file is written. A property whose type cannot be resolved
    /// throws there, which is the one place in this pipeline that fails before a writer runs.
    /// </summary>
    [Fact]
    public void PublicPropertiesOnTheEntryPointDoNotDisturbTheEmittedApplication() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                public string Name { get; set; } = "";
                public int Retries { get; set; }
                public string ReadOnly { get; } = "";
                public static string Shared { get; set; } = "";
            """));

        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// Two applications in one compilation each get their own pair of files. The hint names carry
    /// the entry point name, so two entry points that shared one would silently lose an output —
    /// which is what <c>DuplicateHintNames</c> on the harness watches for.
    /// </summary>
    [Fact]
    public void TwoApplicationsInOneCompilationEachGetTheirOwnPairOfFiles() {
        var result = FunctionGeneratorHarness.Generate(
            FunctionGeneratorHarness.Application(),
            FunctionGeneratorHarness.NamedApplication("Secondary", ns: "TestApp.Second"));

        Assert.Equal(4, result.GeneratedSources.Count);
        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.Contains("Secondary.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.Contains("Application.LambdaHandlerPackage.cs", result.GeneratedSources.Keys);
        Assert.Contains("Secondary.LambdaHandlerPackage.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The emitted halves are partials of the entry point, so the consumer's hand-written half and
    /// the generated ones are a single type. A writer that emitted a differently named or
    /// non-partial class gives every consumer a CS0260.
    /// </summary>
    [Fact]
    public void BothEmittedFilesArePartialsOfTheEntryPointClass() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application());

        Assert.Contains("public partial class Application", result.SourceContaining("LambdaApplication"));
        Assert.Contains("public partial class Application", result.SourceContaining("LambdaHandlerPackage"));
    }

    /// <summary>
    /// Both files are emitted into the entry point's own namespace. Emitting into the wrong one
    /// produces a second <c>Application</c> type rather than the other half of the consumer's.
    /// </summary>
    [Fact]
    public void BothEmittedFilesLandInTheEntryPointsNamespace() {
        var result = FunctionGeneratorHarness.Generate(
            FunctionGeneratorHarness.NamedApplication("Application", ns: "Deeply.Nested.App"));

        Assert.Contains("namespace Deeply.Nested.App", result.SourceContaining("LambdaApplication"));
        Assert.Contains("namespace Deeply.Nested.App", result.SourceContaining("LambdaHandlerPackage"));
    }
}
