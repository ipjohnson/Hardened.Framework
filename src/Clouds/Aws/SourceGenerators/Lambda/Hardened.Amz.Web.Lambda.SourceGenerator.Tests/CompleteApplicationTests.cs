using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// The framework's web pipeline — the half of an API Gateway application that
/// <c>Hardened.Amz.Web.Lambda.SourceGenerator</c> does not write.
///
/// <para>
/// <c>Hardened.SourceGenerator</c> is compiled into this assembly by linked source, so
/// <c>WebIncrementalGenerator</c> is a public type of the assembly under test. The generator that
/// registers it ships in a separate package (<c>Hardened.Web.SourceGenerator</c>), which is what
/// <c>src/LambdaWebTest</c> references alongside this one.
/// </para>
/// </summary>
public class FrameworkWebPipelineGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var entryPoints = context.SyntaxProvider
            .CreateSyntaxProvider(EntryPointSelector.UsingAttribute(), EntryPointSelector.TransformModel(true))
            .WithComparer(new EntryPointSelector.Comparer());

        WebIncrementalGenerator.Setup(context, entryPoints);
    }
}

/// <summary>
/// The two halves of an API Gateway application compiled together, as a consumer's build compiles
/// them.
///
/// <para>
/// An API Gateway application is assembled from two independent generators emitting partials of the
/// same class. <c>Hardened.Amz.Web.Lambda.SourceGenerator</c> writes the entry point that receives
/// the proxy event and hands it to the event processor; <c>Hardened.SourceGenerator</c>'s web
/// pipeline writes the routing table the processor consults and the per-route handler classes behind
/// it. Neither is a whole application, and neither repository's tests compile them together — which
/// is the seam where a partial with a clashing member would show up.
/// </para>
///
/// <para>
/// This is the shape <c>src/LambdaWebTest</c> ships, reproduced without needing that project to run.
/// </para>
/// </summary>
public class CompleteApplicationTests {

    private const string Controller = """
        using System.Threading.Tasks;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp.Controller;

        public class GetMethod {
            [Get("/{author}/{name}")]
            public Task<object> Get(string author, string name) => Task.FromResult<object>(new { });
        }
        """;

    private static Hardened.Amz.SourceGeneration.Testing.GeneratorResult Generate(
        params string[] additionalSources) {
        var result = WebGeneratorHarness.Run(
            [new WebLambdaSourceGenerator(), new FrameworkWebPipelineGenerator()],
            WebGeneratorHarness.Application(),
            additionalSources);

        result.AssertNoErrors();
        WebGeneratorHarness.AssertDidNotCrash(result);

        return result;
    }

    /// <summary>
    /// Every file <c>src/LambdaWebTest</c>'s build produces from these two generators, compiled as
    /// one assembly: the Lambda entry point, the routing table and the route's handler.
    /// </summary>
    [Fact]
    public void TheWholeApplicationCompilesFromBothGeneratorsTogether() {
        var result = Generate(Controller);

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("Routing", StringComparison.Ordinal));
        Assert.Contains(result.GeneratedSources.Keys, key => key.StartsWith("GetMethod_Get", StringComparison.Ordinal));
        Assert.Empty(result.DuplicateHintNames);
    }

    /// <summary>
    /// Both generators emit a partial of <c>Application</c>, and the two must not declare the same
    /// member. The Lambda half brings a service provider field, an event processor field and
    /// <c>RegisterInitDi</c>; the routing half brings its own registration field and method. A
    /// collision on any of them is a CS0102 in the consumer's build and nowhere else.
    /// </summary>
    [Fact]
    public void TheTwoPartialsOfTheApplicationDeclareNoMemberTwice() {
        var result = Generate(Controller);

        var lambdaHalf = result.SourceContaining("App.cs");
        var routingHalf = result.GeneratedSources
            .First(pair => pair.Key.Contains("Routing", StringComparison.Ordinal)).Value;

        Assert.Contains("public partial class Application", lambdaHalf);
        Assert.Contains("public partial class Application", routingHalf);

        // Both halves are compiled above; this pins the one member name they could plausibly share.
        Assert.Contains("RegisterInitDi", lambdaHalf);
        Assert.DoesNotContain("RegisterInitDi", routingHalf);
    }

    /// <summary>
    /// The route is registered against the handler the pipeline emitted, and the Lambda entry point
    /// reaches it only through the event processor. Nothing in the emitted entry point names a route.
    /// </summary>
    [Fact]
    public void TheLambdaEntryPointRoutesOnlyThroughTheEventProcessor() {
        var result = Generate(Controller);

        var lambdaHalf = result.SourceContaining("App.cs");

        WebGeneratorHarness.AssertEmits(lambdaHalf, "return _eventProcessor.Process(request, context);");
        Assert.DoesNotContain("GetMethod", lambdaHalf);
    }

    /// <summary>
    /// Several routes across several controllers in one application. The routing table is emitted
    /// once for the application and names every handler; the entry point is unchanged by any of them.
    /// </summary>
    [Fact]
    public void ManyRoutesAcrossManyControllersStillProduceOneApplicationAndOneRoutingTable() {
        var result = Generate(
            Controller,
            """
            using System.Threading.Tasks;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp.Controller;

            [BasePath("/api/orders")]
            public class OrderController {
                [Get("/{id}")]
                public Task<string> Get(string id) => Task.FromResult(id);

                [Post("/")]
                public Task<string> Create() => Task.FromResult("");

                [Delete("/{id}")]
                public Task Delete(string id) => Task.CompletedTask;
            }
            """);

        Assert.Single(result.GeneratedSources.Keys, key => key == "Application.App.cs");
        Assert.Single(result.GeneratedSources.Keys, key => key.Contains("Routing", StringComparison.Ordinal));

        var handlers = result.GeneratedSources.Keys
            .Count(key => key.StartsWith("OrderController_", StringComparison.Ordinal));

        Assert.Equal(3, handlers);
    }

    /// <summary>
    /// An application with no controllers gets an entry point and a routing table with nothing in it.
    /// Emitting no table at all would leave the event processor resolving a service nobody
    /// registered.
    /// </summary>
    [Fact]
    public void AnApplicationWithNoControllersStillGetsAnEntryPointAndARoutingTable() {
        var result = Generate();

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("Routing", StringComparison.Ordinal));
    }
}
