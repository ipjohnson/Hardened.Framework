using Hardened.SourceGenerator.Function;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// The framework's function pipeline — the half of a Lambda function application that
/// <c>Hardened.Amz.Function.Lambda.SourceGenerator</c> does not write.
///
/// <para>
/// <c>Hardened.SourceGenerator</c> is compiled into this assembly by linked source, so
/// <c>FunctionIncrementalGenerator</c> is a public type of the assembly under test. The generator
/// that registers it ships in a separate package (<c>Hardened.Function.SourceGenerator</c>), which
/// is what <c>src/SqsTest</c> references alongside this one.
/// </para>
/// </summary>
public class FrameworkFunctionPipelineGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var entryPoints = context.SyntaxProvider
            .CreateSyntaxProvider(EntryPointSelector.UsingAttribute(), EntryPointSelector.TransformModel(true))
            .WithComparer(new EntryPointSelector.Comparer());

        FunctionIncrementalGenerator.Setup(context, entryPoints);
    }
}

/// <summary>
/// The two halves of a function application compiled together, as a consumer's build compiles them.
///
/// <para>
/// A Lambda function application is assembled from two independent generators emitting partials of
/// the same class. <c>Hardened.Amz.Function.Lambda.SourceGenerator</c> writes the entry point and the
/// <c>LambdaHandlerPackage</c> that asks each <c>IFunctionHandlerProvider</c> for a handler;
/// <c>Hardened.SourceGenerator</c>'s function pipeline writes the provider that answers, the
/// per-handler invokers behind it, and the registrations that connect them. Neither is a whole
/// application, and neither repository's tests compile them together — which is exactly the seam
/// where a partial with a clashing member or a registration nobody makes would show up.
/// </para>
///
/// <para>
/// This is the shape <c>src/SqsTest</c> ships, reproduced without needing that project to run.
/// </para>
/// </summary>
public class CompleteApplicationTests {

    private const string Model = """
        namespace TestApp.Impl;

        public class DataModel {
            public string Value { get; set; } = "";
        }
        """;

    private static string Handlers(string methods, string className = "SqsFunctionHandler") => $$"""
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;

        namespace TestApp.Impl;

        public class {{className}} {
        {{methods}}
        }
        """;

    private static Hardened.Amz.SourceGeneration.Testing.GeneratorResult Generate(params string[] handlers) {
        var result = FunctionGeneratorHarness.Run(
            [new LambdaFunctionSourceGenerator(), new FrameworkFunctionPipelineGenerator()],
            FunctionGeneratorHarness.Application(),
            [Model, .. handlers]);

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

        return result;
    }

    private const string OneHandler = """
            [HardenedFunction]
            public Task Process(DataModel model) => Task.CompletedTask;
        """;

    /// <summary>
    /// Every file <c>src/SqsTest</c>'s build produces from these two generators, compiled as one
    /// assembly. Four partials of <c>Application</c> and an invoker beside them.
    /// </summary>
    [Fact]
    public void TheWholeApplicationCompilesFromBothGeneratorsTogether() {
        var result = Generate(Handlers(OneHandler));

        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.Contains("Application.LambdaHandlerPackage.cs", result.GeneratedSources.Keys);
        Assert.Contains("Application.FunctionHandlers.cs", result.GeneratedSources.Keys);
        Assert.Contains("Process.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Empty(result.DuplicateHintNames);
    }

    /// <summary>
    /// The seam: the Amz package's <c>LambdaHandlerPackage</c> asks providers for a handler by
    /// function name, and the framework's <c>FunctionHandlerProvider</c> is what answers. If the two
    /// disagreed about the signature the call would not compile — which is the reason to build them
    /// together rather than separately.
    /// </summary>
    [Fact]
    public void TheHandlerPackageCallsTheProviderTheFrameworkPipelineRegisters() {
        var result = Generate(Handlers(OneHandler));

        FunctionGeneratorHarness.AssertEmits(result.SourceContaining("LambdaHandlerPackage"),
            "var handler = provider.GetFunctionHandler(context.FunctionName, serviceProvider);");

        FunctionGeneratorHarness.AssertEmits(result.SourceContaining("FunctionHandlers"),
            "public global::Hardened.Requests.Abstract.Execution.IExecutionRequestHandler? " +
            "GetFunctionHandler(string functionName, global::System.IServiceProvider serviceProvider)");
    }

    /// <summary>
    /// Both halves register through <c>DependencyRegistry&lt;Application&gt;</c>, under different
    /// field and method names. Two generated partials of one class colliding on either name is a
    /// CS0102 in the consumer's build and nowhere else.
    /// </summary>
    [Fact]
    public void TheTwoGeneratorsRegisterThroughTheSameRegistryWithoutColliding() {
        var result = Generate(Handlers(OneHandler));

        FunctionGeneratorHarness.AssertEmits(result.SourceContaining("LambdaHandlerPackage"),
            "private static int _lambdaPackageDi = DependencyRegistry<Application>.Add(LambdaPackageDi)");
        FunctionGeneratorHarness.AssertEmits(result.SourceContaining("FunctionHandlers"),
            "private static int _functionHandlersDi = DependencyRegistry<Application>.Add(FunctionHandlersDI)");
    }

    /// <summary>
    /// The handler's own class is registered as transient by the framework's pipeline, so the
    /// invoker can resolve it. A provider registered without its controller resolves nothing at run
    /// time — which no compilation can catch.
    /// </summary>
    [Fact]
    public void TheHandlersOwnClassIsRegisteredAlongsideTheProvider() {
        var result = Generate(Handlers(OneHandler));

        var registrations = result.SourceContaining("FunctionHandlers");

        // The provider is named in full. It is a nested private class, and from CSharpAuthor 2.0 a
        // bare name would be written global::FunctionHandlerProvider, which resolves to nothing.
        FunctionGeneratorHarness.AssertEmits(registrations,
            "serviceCollection.AddSingleton<" +
            "global::Hardened.Requests.Abstract.Execution.IFunctionHandlerProvider, " +
            "global::TestApp.Application.FunctionHandlerProvider>();");
        FunctionGeneratorHarness.AssertEmits(registrations,
            "serviceCollection.AddTransient<global::TestApp.Impl.SqsFunctionHandler>();");
    }

    /// <summary>
    /// More than one handler in one application: each gets its own invoker, each named function gets
    /// its own case in the provider's switch, and the whole thing still compiles as one assembly.
    /// </summary>
    [Fact]
    public void TwoNamedHandlersInOneApplicationAreEachRoutedByTheirOwnName() {
        var result = Generate(Handlers("""
                [HardenedFunction("order-created")]
                public Task Process(DataModel model) => Task.CompletedTask;

                [HardenedFunction("order-archived")]
                public Task Archive(DataModel model) => Task.CompletedTask;
            """));

        Assert.Contains("order-created.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("order-archived.FunctionHandler.cs", result.GeneratedSources.Keys);

        var provider = result.SourceContaining("FunctionHandlers");

        Assert.Contains("case \"order-created\":", provider);
        Assert.Contains("case \"order-archived\":", provider);
    }

    /// <summary>
    /// Handlers spread across two classes both reach the provider, and each class is registered once.
    /// </summary>
    [Fact]
    public void HandlersOnSeparateClassesAreBothRegisteredAndBothRouted() {
        var result = Generate(
            Handlers("""
                    [HardenedFunction("order-created")]
                    public Task Process(DataModel model) => Task.CompletedTask;
                """),
            Handlers("""
                    [HardenedFunction("order-archived")]
                    public Task Archive(DataModel model) => Task.CompletedTask;
                """, "ArchiveHandler"));

        var registrations = result.SourceContaining("FunctionHandlers");

        FunctionGeneratorHarness.AssertEmits(registrations,
            "serviceCollection.AddTransient<global::TestApp.Impl.SqsFunctionHandler>();");
        FunctionGeneratorHarness.AssertEmits(registrations,
            "serviceCollection.AddTransient<global::TestApp.Impl.ArchiveHandler>();");

        Assert.Contains("case \"order-created\":", registrations);
        Assert.Contains("case \"order-archived\":", registrations);
    }

    /// <summary>
    /// A handler whose function name is its method name is a catch-all: it answers whatever function
    /// name the invocation carried, rather than only its own. That is what makes <c>src/SqsTest</c>'s
    /// unnamed <c>Process</c> handler reachable under whatever the deployed function is called.
    /// </summary>
    [Fact]
    public void AHandlerWithNoExplicitNameAnswersAnyFunctionName() {
        var provider = Generate(Handlers(OneHandler)).SourceContaining("FunctionHandlers");

        Assert.DoesNotContain("switch", provider);
        FunctionGeneratorHarness.AssertEmits(provider,
            "return new global::TestApp.Impl.Generated.SqsFunctionHandler_Process");
    }

    /// <summary>
    /// An application with no handlers at all still gets a provider — it just never returns one. The
    /// <c>LambdaHandlerPackage</c> resolves <c>IEnumerable&lt;IFunctionHandlerProvider&gt;</c>, so an
    /// application emitting no provider would resolve an empty collection and answer null anyway;
    /// emitting one that answers null keeps the registration shape the same either way.
    /// </summary>
    [Fact]
    public void AnApplicationWithNoHandlersStillGetsAProviderThatAnswersNull() {
        var result = FunctionGeneratorHarness.Run(
            [new LambdaFunctionSourceGenerator(), new FrameworkFunctionPipelineGenerator()],
            FunctionGeneratorHarness.Application());

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

        var provider = result.SourceContaining("FunctionHandlers");

        FunctionGeneratorHarness.AssertEmits(provider, "return null;");
    }
}
