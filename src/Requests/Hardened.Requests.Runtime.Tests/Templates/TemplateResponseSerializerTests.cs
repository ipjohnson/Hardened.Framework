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

    private static ITemplateEngine Engine(string? renders = null, string contentType = "text/html") {
        var engine = Substitute.For<ITemplateEngine>();

        engine.CanRender(Arg.Any<string>())
            .Returns(call => renders != null && (string)call[0] == renders);
        engine.ContentTypeFor(Arg.Any<string>())
            .Returns(call => renders != null && (string)call[0] == renders ? contentType : null);

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
    public void CanProduce_FalseWithoutATemplateName() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        Assert.False(serializer.CanProduce("text/html", ContextFor(null)));
    }

    /// <summary>
    /// A name no engine knows is not this serializer's response either. Claiming it here would
    /// turn a missing template into an exception in place of the JSON the request asked for.
    /// </summary>
    [Fact]
    public void CanProduce_FalseWhenNoEngineKnowsTheName() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        Assert.False(serializer.CanProduce("text/html", ContextFor("Missing")));
    }

    [Fact]
    public void CanProduce_TrueWhenTheEngineRendersThatMediaType() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        Assert.True(serializer.CanProduce("text/html", ContextFor("Fortunes")));
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
    /// Reachable when a filter assigns a template name after selection has already run.
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
    /// Never the fallback. A default serializer answers when nothing claims the context, and a
    /// template serializer volunteering there would meet every unmatched request with an exception
    /// about a template name that was never set.
    /// </summary>
    [Fact]
    public void IsDefaultSerializer_IsFalse() {
        Assert.False(new TemplateResponseSerializer(Array.Empty<ITemplateEngine>()).IsDefaultSerializer);
    }

    /// <summary>
    /// Ordered ahead of JSON, so a contested response goes to the template rather than to whichever
    /// implementation type name happened to sort later.
    /// </summary>
    /// <remarks>
    /// Read through the interface, because <c>Order</c> is a default interface member: a serializer
    /// that does not implement it has no such member on the concrete type at all. That is what makes
    /// adding it non-breaking for every serializer already out there.
    /// </remarks>
    [Fact]
    public void Order_IsAheadOfTheJsonSerializers() {
        IResponseSerializer template = new TemplateResponseSerializer(Array.Empty<ITemplateEngine>());
        IResponseSerializer json = new SystemTextJsonResponseSerializer(
            Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration()));

        Assert.True(template.Order < json.Order);
        Assert.Equal((int)ResponseSerializerOrder.Normal, json.Order);
    }

    /// <summary>
    /// The template serializer declines a media type its template does not render, so a client
    /// asking for JSON against a route that names a view gets JSON.
    /// </summary>
    /// <remarks>
    /// It used to claim any response carrying a template name, whatever the client asked for, and
    /// ordering it ahead of JSON made that stick. TechEmpower's json, db, query and update tests all
    /// send an Accept containing <c>text/html;q=0.9</c>, so "does this response name a view" was
    /// never a safe question to answer on its own.
    /// </remarks>
    [Fact]
    public void CanProduce_FalseForAMediaTypeTheTemplateDoesNotRender() {
        var serializer = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        Assert.False(serializer.CanProduce("application/json", ContextFor("Fortunes")));
    }

    /// <summary>
    /// The media type comes from the template, not from the serializer - the same engine renders
    /// html views and plain-text ones.
    /// </summary>
    [Fact]
    public void CanProduce_UsesTheContentTypeTheEngineReportsForThatTemplate() {
        var serializer = new TemplateResponseSerializer(
            new[] { Engine("Receipt", contentType: "text/plain") });

        Assert.True(serializer.CanProduce("text/plain", ContextFor("Receipt")));
        Assert.False(serializer.CanProduce("text/html", ContextFor("Receipt")));
    }

    /// <summary>
    /// A client that will take anything gets the rendered view, because the template serializer is
    /// ordered ahead of JSON and <c>*/*</c> leaves nothing else to decide on.
    /// </summary>
    [Fact]
    public void TheLocatorPicksTheTemplateForAClientThatAcceptsAnything() {
        var json = new SystemTextJsonResponseSerializer(
            Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration()));

        var template = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        var context = Pipeline.Context(accept: "*/*");
        context.Response.TemplateName = "Fortunes";

        var chosen = new SerializationLocatorService(
                Array.Empty<IRequestDeserializer>(),
                new IResponseSerializer[] { template, json })
            .FindResponseSerializer(context);

        Assert.Same(template, chosen);
    }

    /// <summary>And the same pair resolves to JSON when the client asks for JSON.</summary>
    [Fact]
    public void TheLocatorPicksJsonWhenTheClientAsksForIt() {
        var json = new SystemTextJsonResponseSerializer(
            Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration()));

        var template = new TemplateResponseSerializer(new[] { Engine("Fortunes") });

        var context = Pipeline.Context(accept: "application/json,text/html;q=0.9");
        context.Response.TemplateName = "Fortunes";

        var chosen = new SerializationLocatorService(
                Array.Empty<IRequestDeserializer>(),
                new IResponseSerializer[] { template, json })
            .FindResponseSerializer(context);

        Assert.Same(json, chosen);
    }
}
