using Hardened.Requests.Abstract.Outputs;
using Hardened.Templates.RazorBlade.Tests.Models;
using Hardened.Templates.RazorBlade.Tests.Support;
using Xunit;

namespace Hardened.Templates.RazorBlade.Tests;

/// <summary>
/// The base a generated template base derives from, driven through a real compiled view.
/// </summary>
/// <remarks>
/// <para>
/// Everything here rests on two facts about RazorBlade that do not follow from its declarations,
/// and the reason for the odd-looking design is that both of the natural alternatives were tried
/// and failed:
/// </para>
/// <list type="bullet">
/// <item><c>HtmlTemplate&lt;TModel&gt;.Model</c> is read-only, so a model cannot be attached
/// after construction - <c>CS0200</c>.</item>
/// <item>RazorBlade emits the <c>(TModel model) : base(model)</c> constructor only for its own base
/// types, so a custom generic base gets parameterless construction and <c>CS7036</c>.</item>
/// </list>
/// <para>
/// <c>Views.AttachedFortunes</c> compiling at all is the assertion for the first pair, and it is a
/// compile-time one: if the design regresses, this project stops building rather than these tests
/// failing.
/// </para>
/// </remarks>
public class HardenedHtmlTemplateTests {

    private static readonly FortunePage Page = new([new Fortune(1, "hello"), new Fortune(2, "again")]);

    private static IHardenedResponseOutput Template() => new Views.AttachedFortunes();

    /// <summary>
    /// Parameterless construction, then the model off the response. That is the whole shape: a view
    /// is created by a generated factory that cannot know the model type, so the model is read from
    /// where the handler left it.
    /// </summary>
    [Fact]
    public async Task TheModelIsReadFromTheResponseAndRendered() {
        var context = Pipeline.Context(out var body);

        context.Response.ResponseValue = Page;

        await Template().WriteOutput(context);

        var rendered = Pipeline.Rendered(body);

        Assert.Contains("<li>1: hello</li>", rendered);
        Assert.Contains("<li>2: again</li>", rendered);
    }

    /// <summary>
    /// RazorBlade's inherited <c>RenderAsync</c> is what <c>WriteOutput</c> calls, with no adapter
    /// written anywhere.
    /// </summary>
    [Fact]
    public void ARazorBladeTemplateIsAnOutput() {
        Assert.IsAssignableFrom<IHardenedResponseOutput>(new Views.AttachedFortunes());
        Assert.IsAssignableFrom<IHardenedResponseOutput<FortunePage>>(new Views.AttachedFortunes());
    }

    /// <summary>
    /// The content type follows from the base class rather than from a file extension or a
    /// registry, and it is what the view answers <c>SupportsContentType</c> with.
    /// </summary>
    [Fact]
    public async Task TheContentTypeComesFromTheBase() {
        var context = Pipeline.Context(out _);

        context.Response.ResponseValue = Page;

        await Template().WriteOutput(context);

        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
    }

    /// <summary>
    /// A view answers for what it produces, for a wildcard, and for a client that sent no
    /// preference at all.
    /// </summary>
    [Theory]
    [InlineData("text/html")]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("text/*")]
    [InlineData("*/*")]
    [InlineData(null)]
    [InlineData("")]
    public void AViewAnswersWhatItProduces(string? accept) {
        Assert.True(Template().SupportsContentType(accept, Pipeline.Context(out _)));
    }

    /// <summary>
    /// And declines what it does not, which is what turns into a 406 rather than into the model
    /// serialized as JSON.
    /// </summary>
    [Theory]
    [InlineData("application/json")]
    [InlineData("application/xml, text/csv")]
    public void AViewDeclinesWhatItDoesNotProduce(string accept) {
        Assert.False(Template().SupportsContentType(accept, Pipeline.Context(out _)));
    }

    /// <summary>
    /// It answers a header that lists several types when one of them is its own - a browser sends
    /// <c>text/html, application/xhtml+xml, ..., */*</c> and every part of that has to work.
    /// </summary>
    [Fact]
    public void AViewAnswersAHeaderListingSeveralTypes() {
        Assert.True(Template().SupportsContentType(
            "application/json, text/html;q=0.9", Pipeline.Context(out _)));
    }

    /// <summary>
    /// A null model where one is required names the template. Without this guard it surfaces as a
    /// bare cast exception from inside the base, naming nothing - and a handler returning null on
    /// an error path is ordinary, so this is the likely failure rather than an exotic one.
    /// </summary>
    [Fact]
    public async Task ANullModelForAValueTypeNamesTheTemplate() {
        var context = Pipeline.Context(out _);

        IHardenedResponseOutput template = new ValueModelTemplate();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => template.WriteOutput(context));

        Assert.Contains(nameof(ValueModelTemplate), exception.Message);
        Assert.Contains(nameof(Int32), exception.Message);
    }

    /// <summary>And a model of the wrong type says which type it got.</summary>
    [Fact]
    public async Task AMismatchedModelNamesBothTypes() {
        var context = Pipeline.Context(out _);

        context.Response.ResponseValue = "not a fortune page";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Template().WriteOutput(context));

        Assert.Contains(nameof(FortunePage), exception.Message);
        Assert.Contains(nameof(String), exception.Message);
    }

    /// <summary>
    /// A template over a value type, which is the case the null guard exists for - a reference
    /// model legitimately arrives null when a handler returns nothing.
    /// </summary>
    private class ValueModelTemplate : HardenedHtmlTemplate<int> {
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }
}
