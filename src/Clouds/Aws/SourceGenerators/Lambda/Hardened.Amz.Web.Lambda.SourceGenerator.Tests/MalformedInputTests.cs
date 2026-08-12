using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// What the web generators do with source that is incomplete, wrong, or not theirs.
///
/// <para>
/// A generator runs on every keystroke in an IDE, so most of the source it sees is mid-edit. The
/// behaviour asserted here is what actually happens, not what would be ideal — where the two differ
/// it is recorded as such rather than written as an assertion that the defect stays.
/// </para>
/// </summary>
public class MalformedInputTests {

    /// <summary>
    /// A file with no <c>[HardenedModule]</c> class is not this generator's business and produces
    /// nothing at all. A generator emitting an empty application here would give every library in a
    /// solution an API Gateway entry point.
    /// </summary>
    [Fact]
    public void AFileWithNoEntryPointProducesNothing() {
        var result = WebGeneratorHarness.Generate("""
            namespace TestApp;

            public class NotAnApplication {
                public string Value => "x";
            }
            """);

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// A controller with no application. The framework's web generator emits routing for it; the
    /// Lambda generator has no entry point to attach a handler to and emits nothing.
    /// </summary>
    [Fact]
    public void AControllerWithNoApplicationProducesNothing() {
        var result = WebGeneratorHarness.Generate("""
            using System.Threading.Tasks;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp.Controller;

            public class GetMethod {
                [Get("/orders/{id}")]
                public Task<string> Get(string id) => Task.FromResult(id);
            }
            """);

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// An entry point that is not <c>partial</c>. The generator emits a partial of the same name
    /// regardless, so the consumer gets CS0260 pointing at their own declaration — which is the
    /// correct place for it, because adding <c>partial</c> is their fix.
    /// </summary>
    [Fact]
    public void ANonPartialEntryPointFailsWithTheErrorPointingAtTheConsumersDeclaration() {
        var result = WebGeneratorHarness.Run(
            new WebLambdaSourceGenerator(),
            WebGeneratorHarness.Application().Replace("public partial class", "public class"));

        WebGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.Contains(result.Errors, error => error.Id == "CS0260");
    }

    /// <summary>
    /// An entry point missing the <c>CreateServiceProvider</c> half its other generators supply. The
    /// error lands in the emitted file, because that is where the call is — the generator has no way
    /// to know the method is absent, and emitting a call to it is correct.
    /// </summary>
    [Fact]
    public void AnEntryPointWithoutItsServiceProviderHalfFailsInTheEmittedConstructor() {
        var result = WebGeneratorHarness.Run(
            new WebLambdaSourceGenerator(),
            """
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class Application {
            }
            """);

        WebGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains(result.Errors, error =>
            error.Id == "CS0103" &&
            error.Location.GetLineSpan().Path.EndsWith("Application.App.cs", StringComparison.Ordinal));
    }

    /// <summary>
    /// An empty compilation. The generators register a syntax provider that never fires, which has to
    /// be a no-op rather than a crash in the pipeline setup.
    /// </summary>
    [Fact]
    public void AnEmptyCompilationProducesNothingFromEitherWebGenerator() {
        var result = WebGeneratorHarness.RunBoth("namespace TestApp;");

        result.AssertNoErrors();
        WebGeneratorHarness.AssertDidNotCrash(result);

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// An entry point in the global namespace. Unusual but legal C#, and whatever the generator
    /// decides to do about it, it must not emit something that fails to compile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to pass for the wrong reason. <c>EntryPointSelector.TransformModel</c> threw while
    /// building the model, so nothing was emitted and the assertion below held vacuously. The
    /// framework fixed that crash on 2026-08-12, the generator started emitting, and the emitted
    /// application referenced a <c>CreateServiceProvider</c> that this source never declared —
    /// caught in CI rather than locally, because <c>Hardened.SourceGenerator</c> is referenced as a
    /// floating <c>1.0.0-preview*</c> and CI restored the newer package.
    /// </para>
    ///
    /// <para>
    /// The source now comes from the harness, which supplies that method the way the framework's
    /// <c>ServiceProviderFileGenerator</c> does in a real build, so the assertion is about this
    /// generator's own output rather than about a half-built application. It holds whether the
    /// generator emits into the global namespace or declines to.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEntryPointInTheGlobalNamespaceEmitsNothingThatFailsToCompile() {
        var result = WebGeneratorHarness.Run(
            new WebLambdaSourceGenerator(),
            WebGeneratorHarness.NamedApplication("Application", ns: ""));

        Assert.All(result.Errors, error => Assert.All(
            result.GeneratedSources.Keys,
            hint => Assert.False(
                error.Location.GetLineSpan().Path.EndsWith(hint, StringComparison.Ordinal),
                $"the generator emitted {hint}, and it does not compile: {error.GetMessage()}")));
    }

    /// <summary>
    /// A property whose type cannot be resolved. <c>EntryPointSelector</c> reads every public
    /// settable property into the model and throws on one it cannot type, which takes down the whole
    /// generator rather than the property.
    /// </summary>
    /// <remarks>
    /// Recorded 2026-08-12 as a defect: a mid-edit property on the application class costs the
    /// consumer their entire generated entry point, and the diagnostic names the generator rather
    /// than the property. Reported rather than asserted — the assertion below holds either way.
    /// </remarks>
    [Fact]
    public void APropertyOfAnUnresolvableTypeAddsNoErrorInAGeneratedFile() {
        var result = WebGeneratorHarness.Run(
            new WebLambdaSourceGenerator(),
            WebGeneratorHarness.Application("""
                    public NoSuchType Broken { get; set; } = default!;
                """));

        Assert.All(result.Errors, error => Assert.All(
            result.GeneratedSources.Keys,
            hint => Assert.False(
                error.Location.GetLineSpan().Path.EndsWith(hint, StringComparison.Ordinal),
                $"the generator emitted {hint}, and it does not compile: {error.GetMessage()}")));
    }
}
