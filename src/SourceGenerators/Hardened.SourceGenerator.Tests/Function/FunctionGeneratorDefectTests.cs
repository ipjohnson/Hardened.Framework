using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// Three inputs the function generator handles badly, pinned as they behave on 2026-08-12.
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
/// </summary>
public class FunctionGeneratorDefectTests {

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
    public void TwoHandlersWithTheSameFunctionNameCostTheWholeCompilationItsGeneratedCode() {
        var result = FunctionGeneratorHarness.Generate("""
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
            """);

        // Observed 2026-08-12. The right answer is two invokers and a provider; what happens is
        // nothing at all, including for the handler that had no name clash of its own.
        Assert.Empty(result.GeneratedSources);

        var exception = Assert.Single(result.GeneratorExceptions);

        Assert.Contains("Process.FunctionHandler.cs", exception.Message);
        Assert.Contains("must be unique within a generator", exception.Message);
    }

    /// <summary>
    /// The same collision reached through explicit names, which is the form a reader is likelier to
    /// notice — and the form a rename can introduce without touching either handler's body.
    /// </summary>
    [Fact]
    public void TwoHandlersSharingAnExplicitFunctionNameCollideTheSameWay() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction("duplicate")] public void One() { }

                [HardenedFunction("duplicate")] public void Two() { }
            """));

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
    public void AHandlerInTheGlobalNamespaceCrashesTheGenerator() {
        var result = FunctionGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;

            public class GlobalFunctions {
                [HardenedFunction] public void Process() { }
            }
            """);

        // Observed 2026-08-12. The right answer is either a generated handler or a diagnostic
        // naming the unsupported layout; what happens is an unhandled exception and no output.
        Assert.Empty(result.GeneratedSources);

        var exception = Assert.Single(result.GeneratorExceptions);

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Sequence contains no elements", exception.Message);
    }

    /// <summary>
    /// A <c>[FromContext]</c> parameter whose type the compiler cannot resolve crashes the model
    /// transform with a <c>NullReferenceException</c>.
    ///
    /// <para>
    /// <c>FunctionModelGenerator.GetParameterInfoWithBinding</c> writes
    /// <c>parameter.Type?.GetTypeDefinition(context)!</c> — the <c>?.</c> honest about resolution
    /// returning null, the <c>!</c> suppressing the warning that said so, and the dereference two
    /// frames later in <c>CreateRequestParameterInformation</c>. This is the exact pattern the
    /// unresolved-parameter fix removed from <c>BaseRequestModelGenerator.GetParameterInfo</c> on
    /// 2026-08-12; the attributed path in the function generator was not changed with it, so
    /// <c>[FromContext]</c> still reaches it.
    /// </para>
    ///
    /// <para>
    /// It throws out of the syntax transform, so the whole generator contributes nothing. That is
    /// what the earlier fix was for: an editor runs generators over half-written code constantly,
    /// and a parameter type is unresolved for as long as it takes to write the class.
    /// </para>
    /// </summary>
    [Fact]
    public void AFromContextParameterWithAnUnresolvableTypeCrashesTheGenerator() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process([FromContext("id")] NotDeclaredAnywhere id) { }
            """, FunctionGeneratorHarness.FromContextAttributeDeclaration));

        // Observed 2026-08-12. The right answer is the ParameterBindType.Unresolved path: record
        // the parameter, skip this handler, report HOAG010.
        Assert.Empty(result.GeneratedSources);
        Assert.IsType<NullReferenceException>(Assert.Single(result.GeneratorExceptions));
    }

    /// <summary>
    /// An unresolvable parameter on a plain (unattributed) function parameter is handled one step
    /// better and still ends badly: the handler is skipped, and the provider goes on referring to
    /// the invoker that was never emitted.
    ///
    /// <para>
    /// The <c>ParameterBindType.Unresolved</c> fix taught the web generator's routing table to skip
    /// a handler that could not bind — <c>UnresolvableTypeTests.TheRoutingTableDoesNotRouteToThe
    /// HandlerThatWasSkipped</c> is that guarantee. The function generator's provider has no
    /// equivalent guard, so it emits <c>new global::TestApp.Generated.TestFunctions_Process(...)</c>
    /// for a type that does not exist, and the consumer gets CS0234 on generated code they did not
    /// write instead of only the CS0246 they caused.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnresolvableParameterLeavesTheProviderReferencingAHandlerThatWasNeverGenerated() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(NotDeclaredAnywhere model) { }
            """));

        // The invoker is skipped — the binding generator has no case for Unresolved and throws,
        // which SourceGeneratorWrapper turns into an Error-severity diagnostic.
        Assert.DoesNotContain("Process.FunctionHandler.cs", result.GeneratedSources.Keys);

        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "HardenedException" &&
                          diagnostic.GetMessage().Contains("Binding not supported yet: Unresolved"));

        // Observed 2026-08-12: the provider is emitted anyway and still names the missing type.
        var provider = result.SourceContaining("FunctionHandlers.cs");

        Assert.Contains("global::TestApp.Generated.TestFunctions_Process", provider);

        Assert.Contains(result.Errors,
            error => error.Id == "CS0234" && error.GetMessage().Contains("Generated"));
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
    public void OnlyTheFirstUnnamedHandlerIsReachableFromTheProvider() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction] public void First() { }

                [HardenedFunction] public void Second() { }
            """)).AssertNoErrors();

        // Both invokers are generated and both compile.
        Assert.Contains("First.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("Second.FunctionHandler.cs", result.GeneratedSources.Keys);

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
    public void AnExplicitNameMatchingTheMethodNameIsTreatedAsUnnamed() {
        var provider = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction("Process")]
                public void Process() { }
            """)).AssertNoErrors().SourceContaining("FunctionHandlers.cs");

        // Observed 2026-08-12: a catch-all return, not a switch on "Process".
        Assert.DoesNotContain("switch (functionName)", provider);
        Assert.Contains("return new global::TestApp.Generated.TestFunctions_Process(serviceProvider);", provider);
    }

}
