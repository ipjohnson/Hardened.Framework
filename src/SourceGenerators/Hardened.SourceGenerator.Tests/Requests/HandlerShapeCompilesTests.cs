using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Every return shape and arity a handler can have.
///
/// <para>
/// The shape decides which of six constructor overloads and which invoke-method body the generator
/// emits — sync or async, with parameters or without, async-enumerable or not. Each combination is a
/// separate branch through InvokeClassGenerator.CreateConstructor, and until this suite existed only
/// the sync-with-parameters one was ever compiled.
/// </para>
/// </summary>
public class HandlerShapeCompilesTests {

    [Fact]
    public void AVoidHandlerAssignsNoResponseValue() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/ping")]
                public void Ping() { }
            """)).AssertNoErrors();

        var source = result.SourceContaining("Ping");

        Assert.Contains("controller.Ping();", source);
        Assert.DoesNotContain("ResponseValue", source);
    }

    [Fact]
    public void AValueReturningHandlerAssignsTheResponseValue() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/name")]
                public string Name() => "x";
            """)).AssertNoErrors();

        Assert.Contains("context.Response.ResponseValue = controller.Name();", result.SourceContaining("Name"));
    }

    /// <summary>
    /// A bare <c>Task</c> return. The generator rewrites the return type to <c>void</c> while still
    /// marking the handler async, so it must await without assigning a response value.
    /// </summary>
    [Fact]
    public void ATaskReturningHandlerIsAwaitedAndAssignsNoResponseValue() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/task")]
                public Task Work() => Task.CompletedTask;
            """)).AssertNoErrors();

        var source = result.SourceContaining("Work");

        Assert.Contains("await controller.Work();", source);
        Assert.DoesNotContain("ResponseValue", source);
    }

    [Fact]
    public void ATaskOfTReturningHandlerIsAwaitedIntoTheResponseValue() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/task")]
                public Task<string> Work() => Task.FromResult("x");
            """)).AssertNoErrors();

        Assert.Contains("context.Response.ResponseValue = await controller.Work();",
            result.SourceContaining("Work"));
    }

    [Fact]
    public void AValueTaskOfTReturningHandlerIsAwaitedIntoTheResponseValue() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/valuetask")]
                public ValueTask<string> Work() => new ValueTask<string>("x");
            """)).AssertNoErrors();

        Assert.Contains("context.Response.ResponseValue = await controller.Work();",
            result.SourceContaining("Work"));
    }

    [Fact]
    public void AnAsyncHandlerCompiles() {
        RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/async")]
                public async Task<string> Work() {
                    await Task.Yield();
                    return "x";
                }
            """)).AssertNoErrors();
    }

    /// <summary>
    /// An async handler taking parameters. Async and parameters are decided independently, so this
    /// is the fourth corner of that matrix rather than a repeat of either.
    /// </summary>
    [Fact]
    public void AnAsyncHandlerWithParametersUsesTheAsyncParameterisedConstructor() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/async/{id}")]
                public async Task<string> Work(string id) {
                    await Task.Yield();
                    return id;
                }
            """)).AssertNoErrors();

        Assert.Contains("AsyncStandardFilterWithParameters", result.SourceContaining("Work"));
    }

    [Fact]
    public void AnAsyncHandlerWithNoParametersUsesTheAsyncEmptyParameterConstructor() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/async")]
                public async Task<string> Work() {
                    await Task.Yield();
                    return "x";
                }
            """)).AssertNoErrors();

        var source = result.SourceContaining("Work");

        Assert.Contains("AsyncStandardFilterEmptyParameters", source);
        Assert.DoesNotContain("BindRequestParameters", source);
    }

    [Fact]
    public void ASyncHandlerWithNoParametersUsesTheSyncEmptyParameterConstructor() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/health")]
                public string Health() => "ok";
            """)).AssertNoErrors();

        var source = result.SourceContaining("Health");

        Assert.Contains("StandardFilterEmptyParameters", source);
        Assert.DoesNotContain("BindRequestParameters", source);
    }

    /// <summary>
    /// A zero-parameter handler emits no Parameters class and no _parameterInfo array. Anything
    /// referring to either would not compile, which is how the metadata-slot defect surfaced.
    /// </summary>
    [Fact]
    public void AZeroParameterHandlerEmitsNoParametersClass() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/health")]
                public string Health() => "ok";
            """)).AssertNoErrors();

        var source = result.SourceContaining("Health");

        Assert.DoesNotContain("class Parameters", source);
        Assert.DoesNotContain("_parameterInfo", source);
    }

    [Fact]
    public void AnAsyncEnumerableHandlerUsesTheAsyncEnumerableConstructor() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/stream")]
                public async IAsyncEnumerable<string> Stream() {
                    yield return "a";
                    await Task.Yield();
                }
            """)).AssertNoErrors();

        Assert.Contains("AsyncEnumerableFilterEmptyParameters", result.SourceContaining("Stream"));
    }

    [Fact]
    public void AnAsyncEnumerableHandlerWithParametersUsesTheParameterisedConstructor() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/stream/{id}")]
                public async IAsyncEnumerable<string> Stream(string id) {
                    yield return id;
                    await Task.Yield();
                }
            """)).AssertNoErrors();

        Assert.Contains("AsyncEnumerableFilterWithParameters", result.SourceContaining("Stream"));
    }

    /// <summary>A record return type, the shape most handlers actually have.</summary>
    [Fact]
    public void AComplexReturnTypeCompiles() {
        RequestGeneratorHarness.Generate("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record OrderModel(string Sku, int Quantity);

            public class OrderController {
                [Get("/orders")]
                public IReadOnlyList<OrderModel> All() => new List<OrderModel>();

                [Get("/orders/{id}")]
                public Task<OrderModel?> One(string id) => Task.FromResult<OrderModel?>(null);
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// Every shape on one controller. Each handler gets its own invoke class, so a name collision
    /// or a shared static would surface here and nowhere else.
    /// </summary>
    [Fact]
    public void EveryHandlerShapeCoexistsOnOneController() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/void")]
                public void Nothing() { }

                [Get("/task")]
                public Task Work() => Task.CompletedTask;

                [Get("/value")]
                public string Value() => "x";

                [Get("/task-of-t")]
                public Task<string> TaskOfT() => Task.FromResult("x");

                [Get("/value-task")]
                public ValueTask<string> ValueTaskOfT() => new ValueTask<string>("x");

                [Get("/async/{id}")]
                public async Task<string> AsyncWithParameter(string id) {
                    await Task.Yield();
                    return id;
                }

                [Get("/stream")]
                public async IAsyncEnumerable<string> Stream() {
                    yield return "a";
                    await Task.Yield();
                }
            """)).AssertNoErrors();

        Assert.Equal(7, result.GeneratedSources.Count);
    }

    /// <summary>
    /// <c>[RawResponse]</c> commits the response to a content type, which
    /// <c>RawResponseSerializer</c> then claims through ordinary serializer selection.
    /// </summary>
    /// <remarks>
    /// It used to emit a <c>RawOutputHelper.OutputFunc</c> closure as the handler's default output,
    /// which <c>ContextSerializationService</c> consulted ahead of every serializer - so the
    /// attribute pre-empted selection rather than taking part in it. That is what collided with the
    /// template path, and what two copies of a suppression special case existed to work around.
    /// </remarks>
    [Fact]
    public void ARawResponseHandlerCommitsTheResponseContentType() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/raw")]
                [RawResponse("text/csv")]
                public string Raw() => "a,b";
            """)).AssertNoErrors();

        var source = result.SourceContaining("Raw");

        Assert.Contains("Response.ContentType = \"text/csv\"", source);
        Assert.DoesNotContain("RawOutputHelper", source);
    }

    [Fact]
    public void ARawResponseHandlerWithNoContentTypeDefaultsToPlainText() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/raw")]
                [RawResponse]
                public string Raw() => "text";
            """)).AssertNoErrors();

        Assert.Contains("Response.ContentType = \"text/plain\"", result.SourceContaining("Raw"));
    }


    /// <summary>
    /// <c>[Template]</c> and <c>[RawResponse]</c> are read as response information, not as filters,
    /// so neither reaches the metadata array. A handler carrying only one of them has no metadata
    /// at all — which is also the shape that broke the parameters slot.
    /// </summary>
    [Fact]
    public void TemplateAndRawResponseAreNotTreatedAsFilters() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/page")]
                [Template("Index")]
                [RawResponse("text/html")]
                public string Page() => "x";
            """)).AssertNoErrors();

        Assert.DoesNotContain("_metadata", result.SourceContaining("Page"));
    }

    /// <summary>
    /// The template name reaches the response, which is the only thing that makes the annotation
    /// worth carrying.
    /// </summary>
    /// <remarks>
    /// <c>[Template]</c> names a view at build time and whatever renders it runs per request, so
    /// the generated handler is where the two meet. Without this the attribute is read, put on a
    /// model, and dropped - which is the state <c>IExecutionResponse.TemplateName</c> was deleted in
    /// (#101: "never assigned, never read"), and re-adding the property without assigning it would
    /// have earned that verdict a second time.
    /// </remarks>
    [Fact]
    public void ATemplateNameIsPutOnTheResponse() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/page")]
                [Template("Index")]
                public string Page() => "x";
            """)).AssertNoErrors();

        Assert.Contains("context.Response.TemplateName = \"Index\"", result.SourceContaining("Page"));
    }

    /// <summary>
    /// A handler without the attribute assigns nothing, so the property stays null for the
    /// overwhelming majority of handlers rather than being set to an empty string.
    /// </summary>
    [Fact]
    public void AHandlerWithoutATemplateAssignsNothing() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/plain")]
                public string Plain() => "x";
            """)).AssertNoErrors();

        Assert.DoesNotContain("TemplateName", result.SourceContaining("Plain"));
    }
}
