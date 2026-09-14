using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// Inputs the function generator handles badly, pinned as they behave on 2026-08-12.
///
/// <para>
/// These are characterisation tests, not approvals. Each records what the generator does today so
/// that a fix is visible as a failure here rather than as a silent change of behaviour; each is
/// annotated with what the right answer would be. They are grouped in one file so the set is easy
/// to delete when the defects are fixed.
/// </para>
///
/// <para>
/// The first two share a failure mode worth naming: the exception escapes the generator rather than
/// being caught by <c>SourceGeneratorWrapper</c>, so Roslyn reports <c>CS8785</c> and the generator
/// <em>contributes nothing to the whole compilation</em>. Not one handler is lost — all of them are,
/// along with the dependency registration. This is the same failure the web generator's
/// <c>UnresolvableTypeTests</c> exists to prevent, and the reason that fix reported a diagnostic and
/// skipped the single bad handler instead of throwing.
/// </para>
///
/// <para>
/// Two are gone. An unresolvable parameter type, attributed or not, took the whole compilation or
/// left the provider naming an invoker that was never emitted; both now skip the one function and
/// report <c>HOAG010</c>, which is what <see cref="FunctionUnresolvableTypeTests"/> holds them to.
/// </para>
/// </summary>
public class FunctionGeneratorDefectTests
{
    /// <summary>
    /// Two handlers that resolve to the same function name collide on the generated file name, and
    /// the collision takes down the entire generator.
    ///
    /// <para>
    /// The invoker <em>type</em> names are disambiguated — <c>A_Process</c> and <c>B_Process</c> —
    /// but the file name is <c>model.Name.Path + ".FunctionHandler.cs"</c>, which is the function
    /// name alone. Roslyn requires hint names to be unique within a generator and throws from
    /// <c>AdditionalSourcesCollection.Add</c>, which runs when outputs are appended rather than
    /// inside the wrapped delegate — so <c>SourceGeneratorWrapper</c> never sees it.
    /// </para>
    ///
    /// <para>
    /// Two classes each declaring a <c>Process</c> method is an ordinary thing to write. The fix
    /// would be to include the declaring type in the file name, as the type name already does, and
    /// to report a diagnostic when two handlers genuinely claim one function name.
    /// </para>
    /// </summary>
    [Fact]
    public void TwoHandlersWithTheSameFunctionNameCostTheWholeCompilationItsGeneratedCode()
    {
        var result = FunctionGeneratorHarness.Generate(
            """
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class OrderFunctions {
                [HardenedFunction] public void Process() { }
            }

            public class InvoiceFunctions {
                [HardenedFunction] public void Process() { }
            }
            """
        );

        // Observed 2026-08-12. The right answer is two invokers and a provider; what happens is
        // nothing at all, including for the handler that had no name clash of its own.
        Assert.Empty(result.GeneratedSources);

        var exception = Assert.Single(result.GeneratorExceptions);

        Assert.Contains("INVOKE.Process.FunctionHandler.cs", exception.Message);
        Assert.Contains("must be unique within a generator", exception.Message);
    }

    /// <summary>
    /// The same collision reached through explicit names, which is the form a reader is likelier to
    /// notice — and the form a rename can introduce without touching either handler's body.
    /// </summary>
    [Fact]
    public void TwoHandlersSharingAnExplicitFunctionNameCollideTheSameWay()
    {
        var result = FunctionGeneratorHarness.Generate(
            FunctionGeneratorHarness.Application(
                """
                    [HardenedFunction("duplicate")] public void One() { }

                    [HardenedFunction("duplicate")] public void Two() { }
                """
            )
        );

        // Observed 2026-08-12.
        Assert.Empty(result.GeneratedSources);
        Assert.Single(result.GeneratorExceptions);
    }

    /// <summary>
    /// A handler class declared in the global namespace crashes the model transform.
    ///
    /// <para>
    /// <c>BaseRequestModelGenerator.GetControllerType</c> takes <c>.First()</c> of the method's
    /// namespace ancestors, and a type in the global namespace has none —
    /// <c>InvalidOperationException: Sequence contains no elements</c>. It throws out of the syntax
    /// transform, so again the whole generator contributes nothing.
    /// <c>FunctionModelGenerator.GetInvokeHandlerType</c> makes the same assumption a few frames
    /// later and would fail identically.
    /// </para>
    ///
    /// <para>
    /// Related to the <c>GetTypeDefinition</c> global-namespace fix on this branch, and not covered
    /// by it: that one taught the type-definition helper to cope with an empty namespace, while
    /// these two callers still assume a namespace declaration exists in the syntax tree. Top-level
    /// files with no namespace are the default in new .NET templates, so this is reachable by
    /// writing the most obvious possible handler.
    /// </para>
    /// </summary>
    [Fact]
    public void AHandlerInTheGlobalNamespaceCrashesTheGenerator()
    {
        var result = FunctionGeneratorHarness.Generate(
            """
            using Hardened.Requests.Abstract.Attributes;

            public class GlobalFunctions {
                [HardenedFunction] public void Process() { }
            }
            """
        );

        // Observed 2026-08-12. The right answer is either a generated handler or a diagnostic
        // naming the unsupported layout; what happens is an unhandled exception and no output.
        Assert.Empty(result.GeneratedSources);

        var exception = Assert.Single(result.GeneratorExceptions);

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Sequence contains no elements", exception.Message);
    }

    /// <summary>
    /// With more than one unnamed handler, only the first is ever returned — for any function name,
    /// including the others' own method names.
    ///
    /// <para>
    /// <c>CreateFunctionHandlerProviderClass</c> treats every handler whose name equals its method
    /// name as a catch-all and emits <c>defaultHandlers[0]</c>. The second handler's invoker is
    /// generated, compiles, is registered in DI, and is unreachable. Nothing is reported.
    /// </para>
    ///
    /// <para>
    /// Unlike the two collisions above this one is silent — the build is clean and the wrong
    /// handler runs. Two <c>[HardenedFunction]</c> methods without explicit names is the shape any
    /// application gets by adding a second function the same way it added the first.
    /// </para>
    /// </summary>
    [Fact]
    public void OnlyTheFirstUnnamedHandlerIsReachableFromTheProvider()
    {
        var result = FunctionGeneratorHarness
            .Generate(
                FunctionGeneratorHarness.Application(
                    """
                        [HardenedFunction] public void First() { }

                        [HardenedFunction] public void Second() { }
                    """
                )
            )
            .AssertNoErrors();

        // Both invokers are generated and both compile.
        Assert.Contains("INVOKE.First.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("INVOKE.Second.FunctionHandler.cs", result.GeneratedSources.Keys);

        var provider = result.SourceContaining("FunctionHandlers.cs");

        // Observed 2026-08-12: the provider can only ever construct the first.
        Assert.Contains("TestFunctions_First", provider);
        Assert.DoesNotContain("TestFunctions_Second", provider);
    }

    /// <summary>
    /// An explicit name identical to the method name is treated as no name at all, because the
    /// named/unnamed split compares the function name against the method name rather than recording
    /// whether the attribute carried an argument.
    ///
    /// <para>
    /// Harmless on its own — a catch-all still answers to that name. It matters beside a second
    /// unnamed handler, where it decides which of the two wins the single catch-all slot, and it
    /// means <c>[HardenedFunction("Process")]</c> and <c>[HardenedFunction]</c> cannot be told
    /// apart.
    /// </para>
    /// </summary>
    [Fact]
    public void AnExplicitNameMatchingTheMethodNameIsTreatedAsUnnamed()
    {
        var provider = FunctionGeneratorHarness
            .Generate(
                FunctionGeneratorHarness.Application(
                    """
                        [HardenedFunction("Process")]
                        public void Process() { }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("FunctionHandlers.cs");

        // Observed 2026-08-12: a catch-all return, not a switch on "Process".
        Assert.DoesNotContain("switch (scheme + \" \" + path)", provider);
        Assert.Contains(
            "return new global::TestApp.Generated.TestFunctions_Process(serviceProvider);",
            provider
        );
    }
}
