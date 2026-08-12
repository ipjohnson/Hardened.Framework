using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// What the two output stages do when the application entry point is absent, added or moved.
///
/// <para>
/// The stages are wired differently and this is where that shows. Invokers come from the handler
/// provider alone; the registration comes from the entry point <em>combined</em> with the collected
/// handlers, so with no entry point there is nothing to combine against and the whole second stage
/// produces nothing.
/// </para>
/// </summary>
public class FunctionEntryPointTests {

    /// <summary>
    /// A library of handlers with no application of its own. The invokers are still emitted — a
    /// handler library is a legitimate thing to compile, and the application that consumes it
    /// supplies the entry point in its own compilation.
    /// </summary>
    [Fact]
    public void HandlersWithNoEntryPointStillGenerateTheirInvokers() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Handlers("""
                [HardenedFunction]
                public void Process() { }
            """)).AssertNoErrors();

        Assert.Contains("Process.FunctionHandler.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// No entry point means no provider and no registration. Nothing is reported about it: a
    /// handler library without an application is the normal case, not an error.
    /// </summary>
    [Fact]
    public void HandlersWithNoEntryPointGenerateNoProvider() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Handlers("""
                [HardenedFunction]
                public void Process() { }
            """)).AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("FunctionHandlers"));
        Assert.Empty(result.GeneratorDiagnostics);
    }

    /// <summary>
    /// A compilation with neither an entry point nor a handler produces nothing at all, rather than
    /// an empty provider.
    /// </summary>
    [Fact]
    public void ACompilationWithNothingToGenerateProducesNoFiles() {
        var result = FunctionGeneratorHarness.Generate("""
            namespace TestApp;

            public class NotAFunction {
                public void Process() { }
            }
            """).AssertNoErrors();

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// A method without <c>[HardenedFunction]</c> is not a handler, even beside one that is.
    /// </summary>
    [Fact]
    public void OnlyAttributedMethodsBecomeHandlers() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process() { }

                public void NotAHandler() { }
            """)).AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("NotAHandler"));
        Assert.Equal(2, result.GeneratedSources.Count);
    }

    /// <summary>
    /// The entry point and the handlers may be in separate files — the ordinary project layout,
    /// and the one SqsTest has.
    /// </summary>
    [Fact]
    public void TheEntryPointAndItsHandlersMayLiveInSeparateFiles() {
        var result = FunctionGeneratorHarness.Generate(new Dictionary<string, string> {
            ["Application.cs"] = """
                using Hardened.Shared.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                public partial class TestApplication { }
                """,
            ["Functions.cs"] = """
                using Hardened.Requests.Abstract.Attributes;

                namespace TestApp;

                public class TestFunctions {
                    [HardenedFunction] public void Process() { }
                }
                """
        }).AssertNoErrors();

        Assert.Contains("Process.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("TestApplication.FunctionHandlers.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The attribute is matched fully qualified as well as by its short name. Until 2026-08-11
    /// <c>IsAttributed</c> compared against the short name only, so an application written
    /// <c>[Hardened.Shared.Runtime.Attributes.HardenedModule]</c> — which is what a project with a
    /// name clash has to write — was not recognised as an entry point, and its registration was
    /// silently never generated.
    /// </summary>
    [Fact]
    public void AFullyQualifiedModuleAttributeIsStillAnEntryPoint() {
        var result = FunctionGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;

            namespace TestApp;

            [Hardened.Shared.Runtime.Attributes.HardenedModule]
            public partial class TestApplication { }

            public class TestFunctions {
                [HardenedFunction] public void Process() { }
            }
            """).AssertNoErrors();

        Assert.Contains("TestApplication.FunctionHandlers.cs", result.GeneratedSources.Keys);
    }
}
