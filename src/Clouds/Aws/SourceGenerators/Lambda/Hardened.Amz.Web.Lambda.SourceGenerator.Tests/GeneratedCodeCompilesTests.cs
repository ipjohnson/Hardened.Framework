using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// The assertion that matters: what <see cref="WebLambdaSourceGenerator"/> writes is compiled
/// together with the source it was given.
///
/// <para>
/// A test asserting on an emitted string proves the generator produced the characters expected; it
/// does not prove a consumer can build. Three defects in the framework's web generator emitted
/// uncompilable C#, passed every string-matching test, and were caught by integration tests after
/// shipping — see <c>docs/testing-conventions.md</c> §1.
/// </para>
///
/// <para>
/// Every case here also asserts the generator did not crash, which is a separate check: the
/// framework's <c>SourceGeneratorWrapper</c> catches a writer's exception and reports it as a
/// <em>Warning</em>, so a crashed generator emits nothing and still compiles clean.
/// </para>
/// </summary>
public class GeneratedCodeCompilesTests {

    /// <summary>
    /// The shape <c>src/LambdaWebTest</c> ships: a <c>[HardenedModule]</c> class and nothing else.
    /// </summary>
    [Fact]
    public void AnApplicationWithNothingButTheModuleAttributeCompiles() {
        var result = WebGeneratorHarness.Generate(WebGeneratorHarness.Application());

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// A <c>Startup</c> method is handed to <c>StartWithWait</c> as a method group, and it runs
    /// before the event processor is resolved. An application that resolved first would build its
    /// handler from a half-initialised container.
    /// </summary>
    [Fact]
    public void AStartupMethodIsWaitedOnBeforeTheEventProcessorIsResolved() {
        var application = WebGeneratorHarness.Generate(WebGeneratorHarness.Application("""
                private static Task<bool> Startup(IServiceProvider provider) => Task.FromResult(true);
            """)).SourceContaining("App");

        WebGeneratorHarness.AssertEmits(application, "StartWithWait(RootServiceProvider, Startup, 15)");

        var startup = application.IndexOf("StartWithWait", StringComparison.Ordinal);
        var processor = application.IndexOf(
            "_eventProcessor = RootServiceProvider", StringComparison.Ordinal);

        Assert.True(startup < processor, "the event processor is resolved before startup has run");
    }

    [Fact]
    public void AnApplicationWithoutAStartupMethodStartsWithNoStartupTask() {
        WebGeneratorHarness.AssertEmits(
            WebGeneratorHarness.Generate(WebGeneratorHarness.Application()).SourceContaining("App"),
            "StartWithWait(RootServiceProvider, null, 15)");
    }

    /// <summary>
    /// The three logging shapes, which pick different arguments for <c>CreateServiceProvider</c>. The
    /// one-parameter <c>ConfigureLogging</c> goes through as a method group; the two-parameter form
    /// cannot, so the environment is closed over in a lambda first; neither present means no logging
    /// action at all.
    /// </summary>
    [Theory]
    [InlineData("", "CreateServiceProvider(environment, overrideDependencies, null, RegisterInitDi)")]
    [InlineData("    private static void ConfigureLogging(ILoggingBuilder builder) { }",
        "CreateServiceProvider(environment, overrideDependencies, ConfigureLogging, RegisterInitDi)")]
    [InlineData("    private static void ConfigureLogging(IHardenedEnvironment environment, ILoggingBuilder builder) { }",
        "CreateServiceProvider(environment, overrideDependencies, loggingBuilderAction, RegisterInitDi)")]
    public void TheLoggingMethodTheEntryPointDeclaresDecidesWhatIsPassedToTheProviderFactory(
        string member, string expected) {
        WebGeneratorHarness.AssertEmits(
            WebGeneratorHarness.Generate(WebGeneratorHarness.Application(member)).SourceContaining("App"),
            expected);
    }

    /// <summary>
    /// Public settable properties on the entry point are read into the model by
    /// <c>EntryPointSelector</c> before any file is written, which is the one stage of this pipeline
    /// that runs outside the exception wrapper.
    /// </summary>
    [Fact]
    public void PublicPropertiesOnTheEntryPointDoNotDisturbTheEmittedApplication() {
        var result = WebGeneratorHarness.Generate(WebGeneratorHarness.Application("""
                public string Name { get; set; } = "";
                public int Retries { get; set; }
                public string ReadOnly { get; } = "";
                public static string Shared { get; set; } = "";
            """));

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// Two applications in one compilation each get their own file, named after their entry point. A
    /// hint name keyed on anything shared would silently lose one of them.
    /// </summary>
    [Fact]
    public void TwoApplicationsInOneCompilationEachGetTheirOwnFile() {
        var result = WebGeneratorHarness.Generate(
            WebGeneratorHarness.Application(),
            WebGeneratorHarness.NamedApplication("Secondary", ns: "TestApp.Second"));

        Assert.Equal(2, result.GeneratedSources.Count);
        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.Contains("Secondary.App.cs", result.GeneratedSources.Keys);
        Assert.Empty(result.DuplicateHintNames);
    }

    /// <summary>
    /// The emitted class is a partial of the entry point, so the consumer's hand-written half and the
    /// generated one are a single type. A writer that emitted a differently named or non-partial
    /// class gives every consumer a CS0260.
    /// </summary>
    [Fact]
    public void TheEmittedApplicationIsAPartialOfTheEntryPointClass() {
        Assert.Contains("public partial class Application",
            WebGeneratorHarness.Generate(WebGeneratorHarness.Application()).SourceContaining("App"));
    }

    /// <summary>
    /// The file lands in the entry point's own namespace. Emitting into the wrong one produces a
    /// second <c>Application</c> type rather than the other half of the consumer's.
    /// </summary>
    [Fact]
    public void TheEmittedApplicationLandsInTheEntryPointsNamespace() {
        var result = WebGeneratorHarness.Generate(
            WebGeneratorHarness.NamedApplication("Application", ns: "Deeply.Nested.App"));

        Assert.Contains("namespace Deeply.Nested.App", result.SourceContaining("App"));
    }

    /// <summary>
    /// A controller in the compilation is not this generator's business — routing and per-handler
    /// classes come from the framework's web generator. The Lambda generator emits the same
    /// application whether controllers are present or not, and must not fail on them.
    /// </summary>
    [Fact]
    public void ControllersInTheCompilationDoNotChangeTheEmittedApplication() {
        var withController = WebGeneratorHarness.Generate(
            WebGeneratorHarness.Application(),
            """
            using System.Threading.Tasks;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp.Controller;

            public class GetMethod {
                [Get("/{author}/{name}")]
                public Task<object> Get(string author, string name) => Task.FromResult<object>(new { });
            }
            """);

        var withoutController = WebGeneratorHarness.Generate(WebGeneratorHarness.Application());

        Assert.Equal(
            withoutController.SourceContaining("App"), withController.SourceContaining("App"));
    }
}
