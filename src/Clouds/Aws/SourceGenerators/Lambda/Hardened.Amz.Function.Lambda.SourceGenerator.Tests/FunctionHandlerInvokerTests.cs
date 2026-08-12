using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// Runs <see cref="LambdaFunctionModelGenerator"/> and <see cref="LambdaFunctionInvokerFileWriter"/>
/// the way a generator would.
///
/// <para>
/// Both are public, shipped classes of this assembly, and neither is reachable from
/// <see cref="LambdaFunctionSourceGenerator"/> or <see cref="StreamingFunctionLambdaSourceGenerator"/>
/// — nothing in either repository registers them. They are covered here through a driving generator
/// of the same shape the framework's <c>FunctionIncrementalGenerator</c> uses, because what they
/// emit lands in a consumer's build the moment anything does register them.
/// </para>
/// </summary>
public class FunctionHandlerInvokerGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var methodSelector =
            new SyntaxSelector<MethodDeclarationSyntax>(KnownTypes.Requests.HardenedFunctionAttribute);

        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(methodSelector.Where, new LambdaFunctionModelGenerator().GenerateRequestModel)
            .WithComparer(new RequestHandlerModelComparer());

        var entryPoints = context.SyntaxProvider
            .CreateSyntaxProvider(EntryPointSelector.UsingAttribute(), EntryPointSelector.TransformModel(true))
            .Collect();

        var writer = new LambdaFunctionInvokerFileWriter();

        // Deliberately unwrapped. The shipped generators go through the framework's internal
        // SourceGeneratorWrapper, which turns a writer's exception into a Warning; here an exception
        // reaches the driver and the harness reports it, which is what a test wants from a crash.
        context.RegisterSourceOutput(handlers.Combine(entryPoints), writer.GenerateSource);
    }
}

/// <summary>
/// The per-handler invoker: the class that binds an invocation's parameters, calls the consumer's
/// method and carries the metadata the request pipeline reads.
/// </summary>
public class FunctionHandlerInvokerTests {

    private const string Model = """
        namespace TestApp.Impl;

        public class DataModel {
            public string Value { get; set; } = "";

            public int Count { get; set; }
        }
        """;

    private static string Handlers(string methods, string className = "FunctionHandler") => $$"""
        using System;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Requests.Abstract.Execution;

        namespace TestApp.Impl;

        public class {{className}} {
        {{methods}}
        }
        """;

    private static Hardened.Amz.SourceGeneration.Testing.GeneratorResult Generate(params string[] handlers) =>
        FunctionGeneratorHarness.Generate(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            [Model, .. handlers]);

    /// <summary>
    /// The shape <c>src/SqsTest</c> ships: one handler taking a deserialised model. The emitted
    /// invoker derives from the runtime's base handler closed over the consumer's class.
    /// </summary>
    [Fact]
    public void AHandlerTakingAModelCompilesIntoAnInvokerForItsOwnClass() {
        var result = Generate(Handlers("""
                [HardenedFunction]
                public Task Process(DataModel model) => Task.CompletedTask;
            """));

        var invoker = result.SourceContaining("Process.FunctionHandler");

        Assert.Contains(
            "global::Hardened.Requests.Runtime.Execution.BaseExecutionHandler<" +
            "global::TestApp.Impl.FunctionHandler>",
            invoker);
    }

    /// <summary>
    /// An unattributed parameter of a plain class is bound from the request body. This is the only
    /// binding a Lambda function invocation has by default: there is no query string or route to
    /// bind from.
    /// </summary>
    [Fact]
    public void AnUnattributedModelParameterIsDeserialisedFromTheRequestBody() {
        var result = Generate(Handlers("""
                [HardenedFunction]
                public Task Process(DataModel model) => Task.CompletedTask;
            """));

        FunctionGeneratorHarness.AssertEmits(result.SourceContaining("Process.FunctionHandler"),
            "parameters.model = (await contentSerializationService.DeserializeRequestBody<" +
            "global::TestApp.Impl.DataModel>(context))!;");
    }

    /// <summary>
    /// Two handlers on one class produce two invokers, one file each. A writer keying its hint name
    /// on anything shared — the class, the application — would emit one and lose the other.
    /// </summary>
    [Fact]
    public void TwoHandlersOnOneClassEachGetTheirOwnInvoker() {
        var result = Generate(Handlers("""
                [HardenedFunction]
                public Task Process(DataModel model) => Task.CompletedTask;

                [HardenedFunction]
                public Task Archive(DataModel model) => Task.CompletedTask;
            """));

        Assert.Contains("Process.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("Archive.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Empty(result.DuplicateHintNames);
    }

    /// <summary>
    /// Handlers on separate classes are independent, and each invoker names its own controller. A
    /// model generator resolving the enclosing class wrongly would compile — into a call on the wrong
    /// type.
    /// </summary>
    [Fact]
    public void HandlersOnDifferentClassesEachInvokeTheirOwnController() {
        var result = Generate(
            Handlers("""
                    [HardenedFunction]
                    public Task Process(DataModel model) => Task.CompletedTask;
                """),
            Handlers("""
                    [HardenedFunction]
                    public Task Archive(DataModel model) => Task.CompletedTask;
                """, "ArchiveHandler"));

        Assert.Contains("global::TestApp.Impl.FunctionHandler>",
            result.SourceContaining("Process.FunctionHandler"));
        Assert.Contains("global::TestApp.Impl.ArchiveHandler>",
            result.SourceContaining("Archive.FunctionHandler"));
    }

    /// <summary>
    /// Two handlers of the same name on different classes would collide on the emitted hint name —
    /// which is built from the function name alone. Overloads on one class are the same problem in a
    /// smaller space, so the generated invoker <em>type</em> name mixes in the parameter names.
    /// </summary>
    [Fact]
    public void OverloadsOfOneHandlerNameGetDistinctInvokerTypeNames() {
        var result = FunctionGeneratorHarness.Run(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            Model,
            Handlers("""
                    [HardenedFunction("process-model")]
                    public Task Process(DataModel model) => Task.CompletedTask;

                    [HardenedFunction("process-context")]
                    public Task Process(IExecutionContext context) => Task.CompletedTask;
                """));

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

        var types = result.GeneratedSources.Values
            .SelectMany(source => source.Split(Environment.NewLine))
            .Where(line => line.Contains("public partial class FunctionHandler_Process", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Distinct()
            .ToArray();

        Assert.Equal(2, types.Length);
    }

    /// <summary>
    /// The function name is what the runtime's provider switches on, and
    /// <c>[HardenedFunction("name")]</c> is how a consumer decouples it from the method name. Without
    /// this the deployed function would have to be named after the C# method.
    /// </summary>
    [Fact]
    public void AnExplicitFunctionNameReplacesTheMethodNameEverywhereItIsUsed() {
        var result = Generate(Handlers("""
                [HardenedFunction("order-processor")]
                public Task Process(DataModel model) => Task.CompletedTask;
            """));

        Assert.Contains("order-processor.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("\"order-processor\"", result.SourceContaining("order-processor.FunctionHandler"));
    }

    /// <summary>
    /// A function name given as a constant is resolved to its value, not emitted as the expression.
    /// Consumers share the name between the CDK stack and the handler, so it is usually a constant.
    /// </summary>
    [Fact]
    public void AFunctionNameGivenAsAConstantIsResolvedToItsValue() {
        var result = FunctionGeneratorHarness.Generate(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            Model,
            Handlers("""
                    public const string Name = "order-processor";

                    [HardenedFunction(Name)]
                    public Task Process(DataModel model) => Task.CompletedTask;
                """));

        Assert.Contains("order-processor.FunctionHandler.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The method name is the function name when the attribute carries no argument.
    /// </summary>
    [Fact]
    public void AHandlerWithoutAnExplicitNameIsNamedAfterItsMethod() {
        var result = Generate(Handlers("""
                [HardenedFunction]
                public Task Process(DataModel model) => Task.CompletedTask;
            """));

        Assert.Contains("Process.FunctionHandler.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The pipeline services a handler can ask for by type rather than by binding. Each is resolved
    /// from the execution context rather than deserialised, so getting one wrong compiles into a
    /// deserialisation of a framework interface.
    /// </summary>
    [Theory]
    [InlineData("IExecutionContext context")]
    [InlineData("IExecutionRequest request")]
    [InlineData("IExecutionResponse response")]
    [InlineData("IServiceProvider provider")]
    public void EveryPipelineServiceAHandlerCanAskForBindsWithoutDeserialising(string parameter) {
        var result = Generate(Handlers($$"""
                [HardenedFunction]
                public Task Process({{parameter}}) => Task.CompletedTask;
            """));

        var invoker = result.SourceContaining("Process.FunctionHandler");

        Assert.DoesNotContain("DeserializeRequestBody", invoker);
    }

    /// <summary>
    /// A handler taking nothing at all still needs an invoker, and the parameters class it emits has
    /// to compile with an empty parameter list — the shape that broke the sibling web generator when
    /// a handler had metadata but no parameters.
    /// </summary>
    [Fact]
    public void AHandlerTakingNoParametersStillCompiles() {
        var result = Generate(Handlers("""
                [HardenedFunction]
                public Task Ping() => Task.CompletedTask;
            """));

        Assert.Contains("Ping.FunctionHandler.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The return shapes a handler can declare. A synchronous handler must not be awaited and an
    /// asynchronous one must be; the invoker picks between them from the declared return type.
    /// </summary>
    [Theory]
    [InlineData("public Task Process(DataModel model) => Task.CompletedTask;")]
    [InlineData("public Task<string> Process(DataModel model) => Task.FromResult(\"\");")]
    [InlineData("public void Process(DataModel model) { }")]
    [InlineData("public string Process(DataModel model) => \"\";")]
    [InlineData("public async Task<int> Process(DataModel model) { await Task.Yield(); return 1; }")]
    [InlineData("public ValueTask<int> Process(DataModel model) => new ValueTask<int>(1);")]
    public void EveryReturnShapeAHandlerCanDeclareCompiles(string method) {
        Generate(Handlers("    [HardenedFunction]" + Environment.NewLine + "    " + method));
    }

    /// <summary>
    /// A filter attribute on the handler is carried into the emitted invoker's filter list.
    /// <c>[Template]</c>, <c>[RawResponse]</c> and <c>[HardenedFunction]</c> are excluded by
    /// <c>LambdaFunctionModelGenerator.IsFilterAttribute</c> because they configure the handler
    /// rather than wrap it.
    /// </summary>
    [Fact]
    public void AnAttributeThatIsNotHandlerConfigurationBecomesAFilter() {
        var result = FunctionGeneratorHarness.Generate(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            Model,
            """
            namespace TestApp.Impl;

            public class AuditAttribute : System.Attribute { }
            """,
            Handlers("""
                    [HardenedFunction]
                    [Audit]
                    public Task Process(DataModel model) => Task.CompletedTask;
                """));

        Assert.Contains("AuditAttribute", result.SourceContaining("Process.FunctionHandler"));
    }

    /// <summary>
    /// <c>[HardenedFunction]</c> itself is not a filter. Treating it as one would wrap every handler
    /// in an instance of the attribute that marked it.
    /// </summary>
    [Fact]
    public void TheAttributeThatMarksAHandlerIsNotAlsoAFilterOnIt() {
        var result = Generate(Handlers("""
                [HardenedFunction]
                public Task Process(DataModel model) => Task.CompletedTask;
            """));

        Assert.DoesNotContain("HardenedFunctionAttribute", result.SourceContaining("Process.FunctionHandler"));
    }

    /// <summary>
    /// A shipped filter attribute from the Lambda runtime, applied to a handler. This is the case a
    /// consumer actually writes, and the emitted invoker has to name the real type.
    /// </summary>
    [Fact]
    public void AFilterAttributeFromTheLambdaRuntimeIsCarriedIntoTheInvoker() {
        var result = Generate(Handlers("""
                [HardenedFunction]
                [ThrowException]
                public Task Process(DataModel model) => Task.CompletedTask;
            """).Replace(
            "using Hardened.Requests.Abstract.Execution;",
            "using Hardened.Requests.Abstract.Execution;\nusing Hardened.Amz.Function.Lambda.Runtime;"));

        Assert.Contains("ThrowExceptionAttribute", result.SourceContaining("Process.FunctionHandler"));
    }

    /// <summary>
    /// A filter attribute on the handler's <em>class</em> applies to every handler on it. Filters
    /// that only ever applied per method would make a class-wide concern — authorisation, logging —
    /// something a consumer has to repeat on each method and can forget on one.
    /// </summary>
    [Fact]
    public void AFilterAttributeOnTheHandlerClassAppliesToItsHandlers() {
        var result = FunctionGeneratorHarness.Generate(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            Model,
            """
            namespace TestApp.Impl;

            public class AuditAttribute : System.Attribute { }
            """,
            """
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;

            namespace TestApp.Impl;

            [Audit]
            public class FunctionHandler {
                [HardenedFunction]
                public Task Process(DataModel model) => Task.CompletedTask;
            }
            """);

        Assert.Contains("AuditAttribute", result.SourceContaining("Process.FunctionHandler"));
    }

    private static string FromContextHandler(string attribute) => $$"""
        using System.Threading.Tasks;
        using Hardened.Amz.Function.Lambda.Runtime;
        using Hardened.Requests.Abstract.Attributes;

        namespace TestApp.Impl;

        public class FunctionHandler {
            [HardenedFunction]
            public Task Process([{{attribute}}] string requestId) => Task.CompletedTask;
        }
        """;

    /// <summary>
    /// <c>[FromContext]</c> is this repository's own binding source: it takes a value off the Lambda
    /// invocation context rather than the request body. <see cref="LambdaFunctionModelGenerator"/>
    /// maps it onto the pipeline's header binding, which is the only transport-agnostic key/value
    /// source a function invocation has, so the emitted binding must not deserialise.
    /// </summary>
    /// <remarks>
    /// Recorded 2026-08-12, and the reason this case stops short of <c>AssertNoErrors</c>: the
    /// <em>named</em> form emits a double-quoted literal and does not compile.
    /// <c>GetParameterInfoFromAttributes</c> takes the name with
    /// <c>attribute.ArgumentList?.Arguments.FirstOrDefault()?.ToFullString()</c>, which keeps the
    /// quotes the consumer wrote, and the emitter quotes it again — so
    /// <c>[FromContext("AwsRequestId")]</c> becomes
    /// <c>context.Request.Headers.Get(""AwsRequestId"")</c> and two CS1003s. It is the same defect
    /// the framework fixed in its web generator's named binding attributes, in a class that never
    /// received the fix; <c>GetRequestNameModel</c> ten lines above in the same file already does it
    /// correctly through <c>GetConstantValue</c>. Nothing registers this model generator today, so
    /// no consumer hits it yet. The assertion below is written to hold before and after the fix.
    /// </remarks>
    [Fact]
    public void AFromContextParameterIsNotDeserialisedFromTheRequestBody() {
        var result = FunctionGeneratorHarness.Run(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            Model,
            FromContextHandler("FromContext(\"AwsRequestId\")"));

        var invoker = result.SourceContaining("Process.FunctionHandler");

        Assert.DoesNotContain("DeserializeRequestBody", invoker);
        Assert.Contains("AwsRequestId", invoker);
    }

    /// <summary>
    /// <c>[FromContext]</c> with no name. The name is optional on the attribute, and the unnamed form
    /// takes the branch that does not read an argument at all — so unlike the named form above, this
    /// one compiles.
    /// </summary>
    [Fact]
    public void AFromContextParameterWithNoNameCompiles() {
        FunctionGeneratorHarness.Generate(
            new FunctionHandlerInvokerGenerator(),
            FunctionGeneratorHarness.Application(),
            Model,
            FromContextHandler("FromContext"));
    }

    /// <summary>
    /// A parameter with a default value is optional, and the emitted binding keeps the default rather
    /// than requiring every invocation to carry one.
    /// </summary>
    [Fact]
    public void AParameterWithADefaultValueKeepsItInTheEmittedBinding() {
        var result = Generate(Handlers("""
                [HardenedFunction]
                public Task Process(DataModel? model = null) => Task.CompletedTask;
            """));

        Assert.Contains("null", result.SourceContaining("Process.FunctionHandler"));
    }

    /// <summary>
    /// A nullable parameter is not required. Marking it required would reject an invocation that
    /// legitimately omitted it.
    /// </summary>
    [Fact]
    public void ANullableParameterIsNotRequired() {
        Generate(Handlers("""
                [HardenedFunction]
                public Task Process(DataModel? model) => Task.CompletedTask;
            """));
    }
}
