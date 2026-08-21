using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The dispatch <c>InvokeMethodCodeGenerator</c> writes for a declared response set, driven
/// directly.
/// </summary>
/// <remarks>
/// <para>
/// <c>ResponseSetDispatchTests</c> drives the same emitter through a whole generator run and
/// compiles the result, which is the stronger assertion and the one worth having. It also runs in
/// <c>Hardened.Web.SourceGenerator.Tests</c>, against a different generator assembly's copy of this
/// code - and <c>Hardened.Idl.SourceGenerator</c> compiles
/// <c>../Hardened.SourceGenerator/Requests/**</c> in as source rather than referencing it, so that
/// run says nothing about the copy the specification-first generator ships.
/// </para>
/// <para>
/// Calling the emitter with a hand-built model is what exercises the copy this project references.
/// It is also faster and says more precisely which arm is wrong when one is.
/// </para>
/// </remarks>
public class ResponseSetEmitSourceTests {

    private static ITypeDefinition Type(string name) => TypeDefinition.Get("TestApp", name);

    /// <summary>
    /// A handler returning a response set with the given cases, as the model the emitter reads.
    /// </summary>
    private static RequestHandlerModel Handler(params UnionCaseModel[] cases) =>
        new(
            new RequestHandlerNameModel("/todos/{id}", "GET"),
            Type("TodoController"),
            "GetTodo",
            TypeDefinition.Get("TestApp.Generated", "TodoController_GetTodo"),
            [],
            new ResponseInformationModel {
                ReturnType = Type("Response"),
                UnionCases = UnionResponseSelector.Encode(cases)
            },
            []);

    private static string Emit(RequestHandlerModel handler) {
        var file = new CSharpFileDefinition("TestApp.Generated");
        var invokeClass = file.AddClass("Invoke");

        InvokeMethodCodeGenerator.Implement(handler, invokeClass);

        var context = new OutputContext();
        file.WriteOutput(context);

        return context.Output();
    }

    private static UnionCaseModel Case(
        string name, int status, bool headers = false, bool body = true) =>
        new("global::TestApp." + name, status, headers, body);

    #region the switch

    /// <summary>
    /// The payload is assigned once rather than per arm: both sides are already <c>object?</c>, so
    /// nothing transforms it and the arms decide only status, headers and serialization.
    /// </summary>
    [Fact]
    public void ThePayloadIsAssignedOnceFromValue() {
        var emitted = Emit(Handler(Case("Todo", 200), Case("NotFound", 404)));

        Assert.Contains("Response.ResponseValue = __response.Value", emitted);
        Assert.Contains("switch (__response.Value)", emitted);
    }

    [Fact]
    public void EachCaseGetsItsOwnStatus() {
        var emitted = Emit(Handler(Case("Todo", 200), Case("NotFound", 404), Case("Conflict", 409)));

        Assert.Contains("Response.Status = 200", emitted);
        Assert.Contains("Response.Status = 404", emitted);
        Assert.Contains("Response.Status = 409", emitted);
    }

    /// <summary>
    /// Only an arm that contributes headers binds a name and calls ApplyHeaders. A type test per
    /// response is the cost the compile-time switch exists to avoid.
    /// </summary>
    [Fact]
    public void OnlyAHeaderContributingCaseCallsApplyHeaders() {
        var emitted = Emit(Handler(Case("Todo", 200), Case("RateLimited", 429, headers: true)));

        Assert.Contains("__case1.ApplyHeaders(context.Response.Headers)", emitted);
        Assert.Contains("case global::TestApp.Todo _:", emitted);
    }

    /// <summary>
    /// A case nothing reads binds a discard rather than a name, so a consumer's build gets no
    /// unused-variable warning about generated code they cannot edit.
    /// </summary>
    [Fact]
    public void ACaseNothingReadsBindsADiscard() {
        var emitted = Emit(Handler(Case("Todo", 200), Case("NotFound", 404)));

        Assert.Contains("case global::TestApp.NotFound _:", emitted);
        Assert.DoesNotContain("__case", emitted);
    }

    /// <summary>
    /// A bodyless status suppresses serialization on its own arm rather than for the handler.
    /// </summary>
    [Fact]
    public void OnlyABodylessCaseSuppressesSerialization() {
        var emitted = Emit(Handler(Case("Todo", 200), Case("NoContent", 204, body: false)));

        var arms = emitted.Split("case ");

        Assert.Contains(arms, a => a.Contains("NoContent") && a.Contains("ShouldSerialize = false"));
        Assert.Contains(arms, a => a.Contains("Todo") && !a.Contains("ShouldSerialize"));
    }

    /// <summary>
    /// <c>return default;</c> compiles and leaves Value null, so the default arm is reachable from
    /// user code. A success status there would send an empty body under a 200.
    /// </summary>
    [Fact]
    public void TheDefaultArmAnswersFiveHundredWithNoBody() {
        var emitted = Emit(Handler(Case("Todo", 200), Case("NotFound", 404)));

        Assert.Contains("default:", emitted);
        Assert.Contains("Response.Status = 500", emitted);
        Assert.Contains("Response.ShouldSerialize = false", emitted);
    }

    /// <summary>
    /// Arms are emitted in declared order, which is what makes the generated file readable against
    /// the signature - case types cannot be assignable to one another, so order carries no meaning
    /// beyond that.
    /// </summary>
    [Fact]
    public void ArmsKeepTheDeclaredOrder() {
        var emitted = Emit(Handler(Case("Todo", 200), Case("NotFound", 404), Case("Gone", 410)));

        var todo = emitted.IndexOf("TestApp.Todo", StringComparison.Ordinal);
        var notFound = emitted.IndexOf("TestApp.NotFound", StringComparison.Ordinal);
        var gone = emitted.IndexOf("TestApp.Gone", StringComparison.Ordinal);

        Assert.True(todo < notFound && notFound < gone, "Arms must follow the declared order.");
    }

    #endregion

    #region the ordinary path

    /// <summary>
    /// A handler returning one type keeps the single assignment it always had. This is the path
    /// every application in existence takes.
    /// </summary>
    [Fact]
    public void AHandlerWithNoResponseSetIsUnchanged() {
        var handler = new RequestHandlerModel(
            new RequestHandlerNameModel("/todos/{id}", "GET"),
            Type("TodoController"),
            "GetTodo",
            TypeDefinition.Get("TestApp.Generated", "TodoController_GetTodo"),
            [],
            new ResponseInformationModel { ReturnType = Type("Todo") },
            []);

        var emitted = Emit(handler);

        Assert.Contains("Response.ResponseValue", emitted);
        Assert.DoesNotContain("switch (__response.Value)", emitted);
        Assert.DoesNotContain("Response.Status = ", emitted);
    }

    /// <summary>
    /// And a void handler still invokes without assigning anything.
    /// </summary>
    [Fact]
    public void AVoidHandlerAssignsNothing() {
        var handler = new RequestHandlerModel(
            new RequestHandlerNameModel("/todos/{id}", "DELETE"),
            Type("TodoController"),
            "Remove",
            TypeDefinition.Get("TestApp.Generated", "TodoController_Remove"),
            [],
            new ResponseInformationModel { ReturnType = TypeDefinition.Get(typeof(void)) },
            []);

        var emitted = Emit(handler);

        Assert.DoesNotContain("Response.ResponseValue", emitted);
        Assert.DoesNotContain("switch (__response.Value)", emitted);
    }

    #endregion
}
