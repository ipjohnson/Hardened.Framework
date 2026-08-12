using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// What the function generators do with source that is incomplete, wrong, or not theirs.
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
    /// solution a Lambda entry point.
    /// </summary>
    [Fact]
    public void AFileWithNoEntryPointProducesNothing() {
        var result = FunctionGeneratorHarness.Generate("""
            namespace TestApp;

            public class NotAnApplication {
                public string Value => "x";
            }
            """);

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// A handler marked <c>[HardenedFunction]</c> with no application to belong to. The framework's
    /// own function generator combines handlers with the entry point, so this is the state of every
    /// project between adding the first handler and adding the module.
    /// </summary>
    [Fact]
    public void AHandlerWithNoApplicationProducesNoApplicationFiles() {
        var result = FunctionGeneratorHarness.Generate("""
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;

            namespace TestApp.Impl;

            public class FunctionHandler {
                [HardenedFunction]
                public Task Process() => Task.CompletedTask;
            }
            """);

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// A handler with no application anywhere in the compilation writes no invoker. There is nothing
    /// for it to be registered against, and an invoker emitted into a namespace derived from a
    /// missing entry point would name a type nobody declared.
    /// </summary>
    /// <remarks>
    /// Recorded 2026-08-12: <see cref="LambdaFunctionInvokerFileWriter"/> reaches the entry point
    /// through <c>appModel.First()</c>, so it satisfies this by throwing
    /// <see cref="InvalidOperationException"/> rather than by declining. Nothing registers the writer
    /// today, so the state is latent — but whatever does register it inherits the crash, because the
    /// framework's <c>SourceGeneratorWrapper</c> would turn it into a Warning a consumer never reads.
    /// </remarks>
    [Fact]
    public void AHandlerWithNoApplicationInTheCompilationWritesNoInvoker() {
        var result = FunctionGeneratorHarness.Run(
            new FunctionHandlerInvokerGenerator(),
            """
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;

            namespace TestApp.Impl;

            public class FunctionHandler {
                [HardenedFunction]
                public Task Process() => Task.CompletedTask;
            }
            """);

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// A handler parameter naming a type the compiler cannot resolve. The only error a consumer
    /// should see is the compiler's own <c>CS0246</c> for the name they mistyped — the generator must
    /// not turn one unresolvable parameter into a second, generated, failure.
    /// </summary>
    /// <remarks>
    /// Recorded 2026-08-12: today it satisfies that by throwing. <c>BaseRequestModelGenerator</c>
    /// writes <c>parameter.Type?.GetTypeDefinition(context)!</c> — the <c>?.</c> honest about
    /// resolution returning null, the <c>!</c> suppressing the warning that said so — and
    /// dereferences it a few lines later, so the syntax transform raises a
    /// <see cref="NullReferenceException"/> and the whole assembly's output is lost. The framework
    /// fixed this on 2026-08-12 by recording the parameter as unresolved and skipping the handler;
    /// <c>Hardened.Amz</c> pins <c>Hardened.SourceGenerator 1.0.0-preview10151</c>, which predates it.
    /// The assertion is written to hold either way, because both readings agree that nothing broken
    /// may be emitted.
    /// </remarks>
    [Fact]
    public void AHandlerParameterOfAnUnknownTypeAddsNoErrorOfItsOwn() {
        var result = FunctionGeneratorHarness.Run(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            """
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;

            namespace TestApp.Impl;

            public class FunctionHandler {
                [HardenedFunction]
                public Task Process(NoSuchModel model) => Task.CompletedTask;
            }
            """);

        var error = Assert.Single(result.Errors);

        Assert.Equal("CS0246", error.Id);
        Assert.EndsWith("Additional0.cs", error.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same signature alongside a handler that binds fine. Whatever the generator does about the
    /// broken one, the working one must still reach the consumer's build — an assembly losing every
    /// generated type over one mid-edit parameter is the failure mode this guards.
    /// </summary>
    /// <remarks>
    /// Recorded 2026-08-12 as failing: the exception above is raised from the syntax transform, which
    /// runs per handler but takes the whole generator down with it, so neither handler is emitted.
    /// Left as a reported defect rather than an assertion — see the class summary.
    /// </remarks>
    [Fact]
    public void AHandlerThatCannotBindDoesNotEmitAnInvokerForItself() {
        var result = FunctionGeneratorHarness.Run(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            """
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;

            namespace TestApp.Impl;

            public class FunctionHandler {
                [HardenedFunction]
                public Task Save(NoSuchModel model) => Task.CompletedTask;
            }
            """);

        Assert.DoesNotContain("Save.FunctionHandler.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// An entry point that is not <c>partial</c>. The generator emits a partial of the same name
    /// regardless, so the consumer gets CS0260 pointing at their own declaration — which is the
    /// correct place for it, because adding <c>partial</c> is their fix.
    /// </summary>
    [Fact]
    public void ANonPartialEntryPointFailsWithTheErrorPointingAtTheConsumersDeclaration() {
        var result = FunctionGeneratorHarness.Run(
            new LambdaFunctionSourceGenerator(),
            FunctionGeneratorHarness.Application().Replace("public partial class", "public class"));

        FunctionGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.Contains(result.Errors, error => error.Id == "CS0260");
    }

    /// <summary>
    /// An entry point missing the <c>CreateServiceProvider</c> half its other generators supply. The
    /// error lands in the emitted file, because that is where the call is — the generator has no way
    /// to know the method is absent, and emitting a call to it is correct.
    /// </summary>
    [Fact]
    public void AnEntryPointWithoutItsServiceProviderHalfFailsInTheEmittedConstructor() {
        var result = FunctionGeneratorHarness.Run(
            new LambdaFunctionSourceGenerator(),
            """
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class Application {
            }
            """);

        FunctionGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains(result.Errors, error =>
            error.Id == "CS0103" &&
            error.Location.GetLineSpan().Path.EndsWith("Application.LambdaApplication.cs", StringComparison.Ordinal));
    }

    /// <summary>
    /// An empty compilation. The generators register a syntax provider that never fires, which has to
    /// be a no-op rather than a crash in the pipeline setup.
    /// </summary>
    [Fact]
    public void AnEmptyCompilationProducesNothingFromEitherFunctionGenerator() {
        var result = FunctionGeneratorHarness.RunBoth("namespace TestApp;");

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

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
        var result = FunctionGeneratorHarness.Run(
            new LambdaFunctionSourceGenerator(),
            FunctionGeneratorHarness.NamedApplication("Application", ns: ""));

        Assert.All(result.Errors, error => Assert.All(
            result.GeneratedSources.Keys,
            hint => Assert.False(
                error.Location.GetLineSpan().Path.EndsWith(hint, StringComparison.Ordinal),
                $"the generator emitted {hint}, and it does not compile: {error.GetMessage()}")));
    }
}
