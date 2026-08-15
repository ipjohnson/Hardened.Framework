using Hardened.Requests.Abstract.Templates;
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

    private static IHardenedTemplate Template() => new Views.AttachedFortunes();

    /// <summary>
    /// Parameterless construction, then the model. That is the whole shape: a view is created by a
    /// generated factory that cannot know the model type, so the model arrives afterwards.
    /// </summary>
    [Fact]
    public async Task AModelAttachedAfterConstructionRenders() {
        var context = Pipeline.Context(out var body);
        var template = Template();

        template.Attach(Page, context);

        await using var writer = new StreamWriter(body, leaveOpen: true);
        await template.RenderAsync(writer, TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        var rendered = Pipeline.Rendered(body);

        Assert.Contains("<li>1: hello</li>", rendered);
        Assert.Contains("<li>2: again</li>", rendered);
    }

    /// <summary>
    /// RazorBlade's inherited <c>RenderAsync</c> satisfies the interface implicitly, with no
    /// adapter written anywhere. Worth pinning, because nothing in the declarations says it should.
    /// </summary>
    [Fact]
    public void ARazorBladeTemplateSatisfiesTheInterfaceWithNoAdapter() {
        Assert.IsAssignableFrom<IHardenedTemplate>(new Views.AttachedFortunes());
        Assert.IsAssignableFrom<IHardenedTemplate<FortunePage>>(new Views.AttachedFortunes());
    }

    /// <summary>
    /// The content type follows from the base class rather than from a file extension or a
    /// registry, which is what lets the engine ask the template rather than look it up.
    /// </summary>
    [Fact]
    public void TheContentTypeComesFromTheBase() {
        Assert.Equal("text/html; charset=utf-8", Template().ContentType);
    }

    /// <summary>
    /// A null model where one is required names the template. Without this guard it surfaces as a
    /// bare cast exception from inside the base, naming nothing - and a handler returning null on
    /// an error path is ordinary, so this is the likely failure rather than an exotic one.
    /// </summary>
    [Fact]
    public void ANullModelForAValueTypeNamesTheTemplate() {
        var context = Pipeline.Context(out _);

        IHardenedTemplate template = new ValueModelTemplate();

        var exception = Assert.Throws<InvalidOperationException>(() => template.Attach(null, context));

        Assert.Contains(nameof(ValueModelTemplate), exception.Message);
        Assert.Contains(nameof(Int32), exception.Message);
    }

    /// <summary>And a model of the wrong type says which type it got.</summary>
    [Fact]
    public void AMismatchedModelNamesBothTypes() {
        var context = Pipeline.Context(out _);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Template().Attach("not a fortune page", context));

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
