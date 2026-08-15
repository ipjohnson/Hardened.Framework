using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Abstract.Templates;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Templates;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Templates;

/// <summary>
/// Rendering a response that carries a view.
/// </summary>
/// <remarks>
/// There is no engine and no registry here any more. The generated handler puts a factory on the
/// response, this builds the view, hands it the model and asks it to write - so what used to be a
/// name resolved through a dictionary of descriptors is a delegate and a virtual call.
/// </remarks>
public class TemplateResponseSerializerTests {

    /// <summary>A view that records what it was given and writes something recognisable.</summary>
    private class RecordingTemplate : IHardenedTemplate {
        public RecordingTemplate(string contentType = "text/html") {
            ContentType = contentType;
        }

        public string ContentType { get; }

        public object? AttachedModel { get; private set; }

        public IExecutionContext Context { get; private set; } = default!;

        public IExecutionContext? AttachedContext => Context;

        public int Renders { get; private set; }

        public void Attach(object? model, IExecutionContext context) {
            AttachedModel = model;
            Context = context;
        }

        public async Task RenderAsync(TextWriter writer, CancellationToken cancellationToken = default) {
            Renders++;

            await writer.WriteAsync("rendered");
        }
    }

    private static IExecutionContext ContextFor(
        IHardenedTemplate? template, object? responseValue = null, string? accept = null) {
        var context = accept == null ? Pipeline.Context() : Pipeline.Context(accept: accept);

        if (template != null) {
            context.Response.TemplateFactory = _ => template;
        }

        context.Response.ResponseValue = responseValue;

        return context;
    }

    /// <summary>
    /// A response with no view is an ordinary response, and must fall through to the serializer
    /// that would otherwise have handled it.
    /// </summary>
    [Fact]
    public void CanProduce_FalseWithoutATemplate() {
        Assert.False(new TemplateResponseSerializer().CanProduce("text/html", ContextFor(null)));
    }

    [Fact]
    public void CanProduce_TrueWhenTheTemplateProducesThatMediaType() {
        Assert.True(new TemplateResponseSerializer()
            .CanProduce("text/html", ContextFor(new RecordingTemplate())));
    }

    /// <summary>
    /// The response value is the model. Nothing else carries it - the handler's return value is
    /// assigned to <c>ResponseValue</c> and the factory is assigned beside it.
    /// </summary>
    [Fact]
    public async Task SerializeResponse_AttachesTheResponseValueAsTheModel() {
        var template = new RecordingTemplate();
        var model = new { Fortunes = 3 };
        var context = ContextFor(template, model);

        await new TemplateResponseSerializer().SerializeResponse(context);

        Assert.Same(model, template.AttachedModel);
        Assert.Same(context, template.AttachedContext);
        Assert.Equal(1, template.Renders);
    }

    /// <summary>
    /// A filter can replace the view after the handler chose one - a different view for mobile
    /// than for desktop, an A/B test, an error view. That is the dynamic selection a template name
    /// allowed, kept and typed.
    /// </summary>
    [Fact]
    public async Task SerializeResponse_RendersWhicheverFactoryWasAssignedLast() {
        var chosen = new RecordingTemplate();
        var context = ContextFor(new RecordingTemplate());

        context.Response.TemplateFactory = _ => chosen;

        await new TemplateResponseSerializer().SerializeResponse(context);

        Assert.Equal(1, chosen.Renders);
    }

    /// <summary>
    /// The view is built once. Negotiation asks what a template produces once per media type the
    /// client listed, and building one per question would allocate a view for a request that may
    /// not be rendered as one at all.
    /// </summary>
    [Fact]
    public async Task TheTemplateIsBuiltOnceAcrossNegotiationAndRendering() {
        var built = 0;
        var context = Pipeline.Context();

        context.Response.TemplateFactory = _ => {
            built++;

            return new RecordingTemplate();
        };

        var serializer = new TemplateResponseSerializer();

        serializer.CanProduce("application/json", context);
        serializer.CanProduce("text/html", context);

        await serializer.SerializeResponse(context);

        Assert.Equal(1, built);
    }

    /// <summary>Reachable when a filter clears the view after selection has already run.</summary>
    [Fact]
    public async Task SerializeResponse_ThrowsWhenThereIsNoTemplate() {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new TemplateResponseSerializer().SerializeResponse(ContextFor(null)));
    }

    /// <summary>
    /// Never the fallback. A default serializer answers when nothing claims the context, and a
    /// template serializer volunteering there would meet every unmatched request with an exception
    /// about a view that was never chosen.
    /// </summary>
    [Fact]
    public void IsDefaultSerializer_IsFalse() {
        Assert.False(new TemplateResponseSerializer().IsDefaultSerializer);
    }

    /// <summary>
    /// Ordered ahead of JSON, so a contested response goes to the template rather than to whichever
    /// implementation type name happened to sort later.
    /// </summary>
    [Fact]
    public void Order_IsAheadOfTheJsonSerializers() {
        IResponseSerializer template = new TemplateResponseSerializer();
        IResponseSerializer json = new SystemTextJsonResponseSerializer(
            Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration()));

        Assert.True(template.Order < json.Order);
        Assert.Equal((int)ResponseSerializerOrder.Normal, json.Order);
    }

    /// <summary>
    /// The template serializer declines a media type its view does not produce, so a client asking
    /// for JSON against a route that renders a view gets JSON.
    /// </summary>
    /// <remarks>
    /// It used to claim any response carrying a template name, whatever the client asked for, and
    /// ordering it ahead of JSON made that stick. TechEmpower's json, db, query and update tests all
    /// send an Accept containing <c>text/html;q=0.9</c>, so "does this response name a view" was
    /// never a safe question to answer on its own.
    /// </remarks>
    [Fact]
    public void CanProduce_FalseForAMediaTypeTheTemplateDoesNotProduce() {
        Assert.False(new TemplateResponseSerializer()
            .CanProduce("application/json", ContextFor(new RecordingTemplate())));
    }

    /// <summary>
    /// The media type comes from the view, not from the serializer - an HTML base and a plain-text
    /// base render the same way and answer differently.
    /// </summary>
    [Fact]
    public void CanProduce_UsesTheContentTypeTheTemplateReports() {
        var serializer = new TemplateResponseSerializer();

        Assert.True(serializer.CanProduce("text/plain", ContextFor(new RecordingTemplate("text/plain"))));
        Assert.False(serializer.CanProduce("text/html", ContextFor(new RecordingTemplate("text/plain"))));
    }

    /// <summary>
    /// The view fills in a blank content type rather than overwriting one a handler committed to.
    /// </summary>
    [Fact]
    public async Task SerializeResponse_DoesNotOverwriteAContentTypeTheHandlerChose() {
        var context = ContextFor(new RecordingTemplate());

        context.Response.ContentType = "text/html; charset=iso-8859-1";

        await new TemplateResponseSerializer().SerializeResponse(context);

        Assert.Equal("text/html; charset=iso-8859-1", context.Response.ContentType);
    }

    /// <summary>
    /// A client that will take anything gets the rendered view, because the template serializer is
    /// ordered ahead of JSON and <c>*/*</c> leaves nothing else to decide on.
    /// </summary>
    [Fact]
    public void TheLocatorPicksTheTemplateForAClientThatAcceptsAnything() {
        var json = new SystemTextJsonResponseSerializer(
            Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration()));

        var template = new TemplateResponseSerializer();

        var chosen = new SerializationLocatorService(
                Array.Empty<IRequestDeserializer>(),
                new IResponseSerializer[] { template, json })
            .FindResponseSerializer(ContextFor(new RecordingTemplate(), accept: "*/*"));

        Assert.Same(template, chosen);
    }

    /// <summary>And the same pair resolves to JSON when the client asks for JSON.</summary>
    [Fact]
    public void TheLocatorPicksJsonWhenTheClientAsksForIt() {
        var json = new SystemTextJsonResponseSerializer(
            Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration()));

        var template = new TemplateResponseSerializer();

        var chosen = new SerializationLocatorService(
                Array.Empty<IRequestDeserializer>(),
                new IResponseSerializer[] { template, json })
            .FindResponseSerializer(
                ContextFor(new RecordingTemplate(), accept: "application/json,text/html;q=0.9"));

        Assert.Same(json, chosen);
    }
}
