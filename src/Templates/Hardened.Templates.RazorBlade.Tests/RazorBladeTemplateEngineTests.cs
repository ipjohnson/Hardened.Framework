using Hardened.Templates.RazorBlade.Impl;
using Hardened.Templates.RazorBlade.Tests.Models;
using Hardened.Templates.RazorBlade.Tests.Support;
using Xunit;

namespace Hardened.Templates.RazorBlade.Tests;

/// <summary>
/// Rendering a real .cshtml, compiled by RazorBlade into this assembly, through the engine.
/// </summary>
public class RazorBladeTemplateEngineTests {

    private static readonly FortunePage Page = new([
        new Fortune(1, "hello"),
        new Fortune(2, "<b>escape me</b>")
    ]);

    private static RazorBladeTemplateEngine Engine(params RazorBladeTemplateDescriptor[] templates) =>
        new([new Source(templates)]);

    private static RazorBladeTemplateEngine Standard() =>
        Engine(
            RazorBladeTemplate.Html<FortunePage>("Fortunes", model => new Views.Fortunes(model)),
            RazorBladeTemplate.PlainText<FortunePage>("Receipt", model => new Views.Receipt(model)));

    private sealed class Source(IEnumerable<RazorBladeTemplateDescriptor> templates) : IRazorBladeTemplateSource {
        public IEnumerable<RazorBladeTemplateDescriptor> Templates { get; } = templates;
    }

    [Fact]
    public async Task RenderAsync_WritesTheTemplateOutputToTheResponseBody() {
        var context = Pipeline.Context(out var body);

        await Standard().RenderAsync("Fortunes", Page, context);

        Assert.Contains("<td>hello</td>", Pipeline.Rendered(body));
    }

    /// <summary>
    /// The whole reason to render through a template type rather than string concatenation.
    /// </summary>
    [Fact]
    public async Task RenderAsync_HtmlEncodesModelValues() {
        var context = Pipeline.Context(out var body);

        await Standard().RenderAsync("Fortunes", Page, context);

        var rendered = Pipeline.Rendered(body);

        Assert.Contains("&lt;b&gt;escape me&lt;/b&gt;", rendered);
        Assert.DoesNotContain("<b>escape me</b>", rendered);
    }

    /// <summary>
    /// A BOM ahead of the markup is the failure this guards. StreamWriter's parameterless UTF8
    /// encoding writes one, it renders as a stray character in the browser, and it is invisible
    /// in any assertion that compares decoded strings rather than bytes.
    /// </summary>
    [Fact]
    public async Task RenderAsync_WritesNoByteOrderMark() {
        var context = Pipeline.Context(out var body);

        await Standard().RenderAsync("Fortunes", Page, context);

        Assert.Equal((byte)'<', body.ToArray()[0]);
    }

    [Fact]
    public async Task RenderAsync_SetsTheContentTypeFromTheTemplateBaseType() {
        var context = Pipeline.Context(out _);

        await Standard().RenderAsync("Fortunes", Page, context);

        Assert.Equal(RazorBladeTemplate.HtmlContentType, context.Response.ContentType);
    }

    /// <summary>
    /// A plain-text template is not HTML, and the content type follows the base type rather than
    /// the file extension - both files here are .cshtml.
    /// </summary>
    [Fact]
    public async Task RenderAsync_PlainTextTemplateReportsTextPlainAndDoesNotEncode() {
        var context = Pipeline.Context(out var body);

        await Standard().RenderAsync("Receipt", Page, context);

        Assert.Equal(RazorBladeTemplate.PlainTextContentType, context.Response.ContentType);
        Assert.Contains("<b>escape me</b>", Pipeline.Rendered(body));
    }

    /// <summary>
    /// A handler that set a content type meant it. Overwriting it here would make an override
    /// impossible to express.
    /// </summary>
    [Fact]
    public async Task RenderAsync_DoesNotOverwriteAContentTypeTheHandlerSet() {
        var context = Pipeline.Context(out _);
        context.Response.ContentType = "application/xhtml+xml";

        await Standard().RenderAsync("Fortunes", Page, context);

        Assert.Equal("application/xhtml+xml", context.Response.ContentType);
    }

    /// <summary>
    /// The ASP.NET Core host coerces a null content type to "", so a response that has been
    /// touched and left unset reads back as empty rather than null. Treating that as "already
    /// set" would ship every template response with no content type at all.
    /// </summary>
    [Fact]
    public async Task RenderAsync_TreatsAnEmptyContentTypeAsUnset() {
        var context = Pipeline.Context(out _);
        context.Response.ContentType = "";

        await Standard().RenderAsync("Fortunes", Page, context);

        Assert.Equal(RazorBladeTemplate.HtmlContentType, context.Response.ContentType);
    }

    [Fact]
    public void CanRender_IsCaseInsensitive() {
        var engine = Standard();

        Assert.True(engine.CanRender("fortunes"));
        Assert.True(engine.CanRender("FORTUNES"));
    }

    [Fact]
    public void CanRender_FalseForAnUnknownName() {
        Assert.False(Standard().CanRender("Missing"));
    }

    /// <summary>
    /// The name is in the message, and so is what was available - a template that failed to
    /// register otherwise produces a miss with nothing to compare against.
    /// </summary>
    [Fact]
    public async Task RenderAsync_UnknownNameNamesTheKnownTemplates() {
        var context = Pipeline.Context(out _);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Standard().RenderAsync("Missing", Page, context));

        Assert.Contains("Missing", exception.Message);
        Assert.Contains("Fortunes", exception.Message);
    }

    /// <summary>
    /// Sources are merged so a library can ship views beside the application's.
    /// </summary>
    [Fact]
    public void Templates_FromEverySourceAreReachable() {
        var engine = new RazorBladeTemplateEngine([
            new Source([RazorBladeTemplate.Html<FortunePage>("Fortunes", model => new Views.Fortunes(model))]),
            new Source([RazorBladeTemplate.PlainText<FortunePage>("Receipt", model => new Views.Receipt(model))])
        ]);

        Assert.True(engine.CanRender("Fortunes"));
        Assert.True(engine.CanRender("Receipt"));
    }

    /// <summary>
    /// Later registration wins, so an application can replace a view a library shipped.
    /// </summary>
    [Fact]
    public async Task Templates_ALaterSourceReplacesAnEarlierOneOfTheSameName() {
        var engine = new RazorBladeTemplateEngine([
            new Source([RazorBladeTemplate.Html<FortunePage>("Page", model => new Views.Fortunes(model))]),
            new Source([RazorBladeTemplate.PlainText<FortunePage>("Page", model => new Views.Receipt(model))])
        ]);

        var context = Pipeline.Context(out _);

        await engine.RenderAsync("Page", Page, context);

        Assert.Equal(RazorBladeTemplate.PlainTextContentType, context.Response.ContentType);
    }

    /// <summary>
    /// The response body stays usable after a render. Disposing a StreamWriter closes the stream
    /// underneath it unless it was opened with leaveOpen, and the host still has headers and
    /// completion to write.
    /// </summary>
    [Fact]
    public async Task RenderAsync_LeavesTheResponseBodyOpen() {
        var context = Pipeline.Context(out var body);

        await Standard().RenderAsync("Fortunes", Page, context);

        Assert.True(body.CanWrite);
    }
}
