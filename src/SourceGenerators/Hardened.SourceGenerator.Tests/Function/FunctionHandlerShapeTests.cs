using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// Every return shape and arity a <c>[HardenedFunction]</c> handler can have.
///
/// <para>
/// A function handler goes through the same InvokeClassGenerator as a web route, so the shape picks
/// the same six constructor overloads — sync or async, with parameters or without. What differs is
/// everything around it: the function generator supplies its own name model, its own invoker type
/// name and its own file name, and none of that had ever been compiled before this suite.
/// </para>
/// </summary>
public class FunctionHandlerShapeTests {

    [Fact]
    public void AVoidHandlerAssignsNoResponseValue() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process() { }
            """)).AssertNoErrors();

        var source = result.SourceContaining("Process.FunctionHandler");

        Assert.Contains("controller.Process();", source);
        Assert.DoesNotContain("ResponseValue", source);
    }

    [Fact]
    public void AValueReturningHandlerAssignsTheResponseValue() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public string Process() => "x";
            """)).AssertNoErrors();

        Assert.Contains("context.Response.ResponseValue = controller.Process();",
            result.SourceContaining("Process.FunctionHandler"));
    }

    /// <summary>
    /// A bare <c>Task</c> return — the shape SqsTest's handler actually has. The generator rewrites
    /// the return type to <c>void</c> while still marking the handler async, so it must await
    /// without assigning a response value.
    /// </summary>
    [Fact]
    public void ATaskReturningHandlerIsAwaitedAndAssignsNoResponseValue() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public Task Process() => Task.CompletedTask;
            """)).AssertNoErrors();

        var source = result.SourceContaining("Process.FunctionHandler");

        Assert.Contains("await controller.Process();", source);
        Assert.DoesNotContain("ResponseValue", source);
    }

    [Fact]
    public void ATaskOfTReturningHandlerIsAwaitedIntoTheResponseValue() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public Task<string> Process() => Task.FromResult("x");
            """)).AssertNoErrors();

        Assert.Contains("context.Response.ResponseValue = await controller.Process();",
            result.SourceContaining("Process.FunctionHandler"));
    }

    [Fact]
    public void AValueTaskOfTReturningHandlerIsAwaitedIntoTheResponseValue() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public ValueTask<string> Process() => new ValueTask<string>("x");
            """)).AssertNoErrors();

        Assert.Contains("context.Response.ResponseValue = await controller.Process();",
            result.SourceContaining("Process.FunctionHandler"));
    }

    /// <summary>
    /// The exact signature SqsTest declares: <c>async Task</c> taking one payload model. If any
    /// single test here stands for "the shipped consumer still builds", it is this one.
    /// </summary>
    [Fact]
    public void AnAsyncTaskHandlerTakingAModelCompiles() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public async Task Process(DataModel model) {
                    await Task.Yield();
                }
            """, FunctionGeneratorHarness.SupportTypes)).AssertNoErrors();

        var source = result.SourceContaining("Process.FunctionHandler");

        Assert.Contains("await controller.Process(parameters.model);", source);
        Assert.Contains("AsyncStandardFilterWithParameters", source);
    }

    [Fact]
    public void AZeroParameterHandlerUsesTheEmptyParameterConstructor() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public string Process() => "x";
            """)).AssertNoErrors();

        var source = result.SourceContaining("Process.FunctionHandler");

        Assert.Contains("StandardFilterEmptyParameters", source);
        Assert.DoesNotContain("BindRequestParameters", source);
    }

    /// <summary>
    /// A zero-parameter handler emits no Parameters class and no <c>_parameterInfo</c> array.
    /// Anything referring to either would not compile — that is how the metadata-slot defect
    /// surfaced on the web side.
    /// </summary>
    [Fact]
    public void AZeroParameterHandlerEmitsNoParametersClass() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process() { }
            """)).AssertNoErrors().SourceContaining("Process.FunctionHandler");

        Assert.DoesNotContain("class Parameters", source);
        Assert.DoesNotContain("_parameterInfo", source);
    }

    [Fact]
    public void AnAsyncHandlerWithNoParametersUsesTheAsyncEmptyParameterConstructor() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public async Task<string> Process() {
                    await Task.Yield();
                    return "x";
                }
            """)).AssertNoErrors().SourceContaining("Process.FunctionHandler");

        Assert.Contains("AsyncStandardFilterEmptyParameters", source);
        Assert.DoesNotContain("BindRequestParameters", source);
    }

    /// <summary>
    /// Several parameters at once. The invoke call has to list them in declaration order — a
    /// reordering compiles whenever two parameters share a type and fails silently at run time.
    /// </summary>
    [Fact]
    public void SeveralParametersAreInvokedInDeclarationOrder() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public Task<string> Process(DataModel model, IThing thing, IExecutionContext context) =>
                    Task.FromResult("x");
            """, FunctionGeneratorHarness.SupportTypes))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        var invoke = source.Substring(source.IndexOf("await controller.Process(", StringComparison.Ordinal));

        Assert.True(
            invoke.IndexOf("parameters.model", StringComparison.Ordinal) <
            invoke.IndexOf("parameters.thing", StringComparison.Ordinal),
            "the model is declared first and must be passed first");

        Assert.True(
            invoke.IndexOf("parameters.thing", StringComparison.Ordinal) <
            invoke.IndexOf("parameters.context", StringComparison.Ordinal),
            "the service is declared second and must be passed second");
    }

    /// <summary>
    /// The indexer on the generated Parameters class, which the filter pipeline uses to read and
    /// rewrite arguments by position. One case per parameter, or a filter silently reads the wrong
    /// one.
    /// </summary>
    [Fact]
    public void EachParameterGetsItsOwnIndexerCase() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(DataModel model, IThing thing) { }
            """, FunctionGeneratorHarness.SupportTypes))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("return model!;", source);
        Assert.Contains("return thing!;", source);
        Assert.Contains("case 0:", source);
        Assert.Contains("case 1:", source);
    }

    /// <summary>A record payload, the shape most function models actually have.</summary>
    [Fact]
    public void ARecordPayloadCompiles() {
        FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public Task<OrderModel> Process(OrderModel order) => Task.FromResult(order);
            """, "public record OrderModel(string Sku, int Quantity);")).AssertNoErrors();
    }

    /// <summary>
    /// Every shape on one handler class. Each gets its own invoker, so a name collision or a shared
    /// static surfaces here and nowhere else.
    /// </summary>
    [Fact]
    public void EveryHandlerShapeCoexistsOnOneClass() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction] public void Nothing() { }

                [HardenedFunction] public Task Bare() => Task.CompletedTask;

                [HardenedFunction] public string Value() => "x";

                [HardenedFunction] public Task<string> TaskOfT() => Task.FromResult("x");

                [HardenedFunction] public ValueTask<string> ValueTaskOfT() => new ValueTask<string>("x");

                [HardenedFunction]
                public async Task<string> WithParameter(DataModel model) {
                    await Task.Yield();
                    return model.Value;
                }
            """, FunctionGeneratorHarness.SupportTypes)).AssertNoErrors();

        // Six invokers plus the one provider file.
        Assert.Equal(7, result.GeneratedSources.Count);
    }

    /// <summary>
    /// Two overloads of one method name, told apart by an explicit function name. The invoker type
    /// name carries a hash of the parameter identifiers precisely so these do not collide.
    /// </summary>
    [Fact]
    public void OverloadsWithDistinctFunctionNamesGetDistinctInvokerTypes() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction("no-args")]
                public void Process() { }

                [HardenedFunction("with-args")]
                public void Process(DataModel model) { }
            """, FunctionGeneratorHarness.SupportTypes)).AssertNoErrors();

        Assert.Contains("no-args.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("with-args.FunctionHandler.cs", result.GeneratedSources.Keys);

        // The parameterless one keeps the bare name; the other is suffixed with the parameter hash.
        var provider = result.SourceContaining("FunctionHandlers.cs");

        Assert.Contains("TestFunctions_Process(serviceProvider)", provider);
        Assert.Contains("TestFunctions_Process_", provider);
    }
}
