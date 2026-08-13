using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Abstract.Templates;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Templates;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Templates;

/// <summary>
/// Routing a response that names a template to an engine that can render it.
/// </summary>
public class TemplateResponseSerializerTests {

    private static ITemplateEngine Engine(string? renders = null) {
        var engine = Substitute.For<ITemplateEngine>();

        engine.CanRender(Arg.Any<string>())
            .Returns(call => renders != null && (string)call[0] == renders);

        return engine;
    }

    private static IExecutionContext ContextFor(string? templateName, object? responseValue = null) {
        var context = Pipeline.Context();

        context.Response.TemplateName = templateName;
        context.Response.ResponseValue = responseValue;

        return context;
    }

    /// <summary>
    /// A response with no template name is an ordinary response, and must fall through to the
    /// serializer that would otherwise have handled it.
    /// </summary>
    [Fact]
    public void CanProcessContext_FalseWithoutATemplateName() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        Assert.False(serializer.CanProcessContext(ContextFor(null)));
    }

    /// <summary>
    /// A name no engine knows is not this serializer's response either. Claiming it here would
    /// turn a missing template into an exception in place of the JSON the request asked for.
    /// </summary>
    [Fact]
    public void CanProcessContext_FalseWhenNoEngineKnowsTheName() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        Assert.False(serializer.CanProcessContext(ContextFor("Missing")));
    }

    [Fact]
    public void CanProcessContext_TrueWhenAnEngineKnowsTheName() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        Assert.True(serializer.CanProcessContext(ContextFor("Fortunes")));
    }

    /// <summary>
    /// The response value is the model. Nothing else carries it - the handler's return value is
    /// assigned to <c>ResponseValue</c> and the template name is assigned beside it.
    /// </summary>
    [Fact]
    public async Task SerializeResponse_PassesTheResponseValueAsTheModel() {
        var engine = Engine("Fortunes");
        var model = new { Fortunes = 3 };
        var context = ContextFor("Fortunes", model);

        await new TemplateResponseSerializer(new[] { engine }).SerializeResponse(context);

        await engine.Received(1).RenderAsync("Fortunes", model, context);
    }

    /// <summary>
    /// Engines are tested in reverse registration order, so an application's engine is asked
    /// before one the framework registered. This mirrors SerializationLocatorService rather than
    /// inventing a second ordering rule.
    /// </summary>
    [Fact]
    public async Task SerializeResponse_TheLastRegisteredEngineThatClaimsTheNameWins() {
        var framework = Engine("Fortunes");
        var application = Engine("Fortunes");
        var context = ContextFor("Fortunes");

        await new TemplateResponseSerializer(new[] { framework, application }).SerializeResponse(context);

        await application.Received(1).RenderAsync("Fortunes", Arg.Any<object?>(), context);
        await framework.DidNotReceive().RenderAsync(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// An engine that does not claim the name is skipped rather than being handed a name it
    /// already said it could not render.
    /// </summary>
    [Fact]
    public async Task SerializeResponse_SkipsEnginesThatDoNotClaimTheName() {
        var declines = Engine("Other");
        var claims = Engine("Fortunes");
        var context = ContextFor("Fortunes");

        await new TemplateResponseSerializer(new[] { claims, declines }).SerializeResponse(context);

        await claims.Received(1).RenderAsync("Fortunes", Arg.Any<object?>(), context);
        await declines.DidNotReceive().RenderAsync(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// Reachable when a filter assigns a template name after CanProcessContext has already run.
    /// </summary>
    [Fact]
    public async Task SerializeResponse_ThrowsWhenNoEngineClaimsTheName() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer.SerializeResponse(ContextFor("Missing")));

        Assert.Contains("Missing", exception.Message);
    }

    [Fact]
    public async Task SerializeResponse_ThrowsWhenThereIsNoTemplateName() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer.SerializeResponse(ContextFor(null)));
    }

    /// <summary>
    /// It is not an <c>IResponseSerializer</c>, so the locator cannot reach it at all - which is
    /// the point. Routing template responses through the locator made <c>/fortunes</c> return a
    /// JSON-serialized model, because a request carrying <c>Accept: application/json</c> and a
    /// template name satisfies both candidates and the locator answers with whichever was
    /// registered later. Both are registered by one module, so nothing could reorder them.
    /// </summary>
    [Fact]
    public void ItIsNotAnIResponseSerializer_SoTheLocatorCannotChooseItByAccident() {
        Assert.False(typeof(IResponseSerializer).IsAssignableFrom(typeof(TemplateResponseSerializer)));
    }
}
