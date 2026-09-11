using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.OpenApi;

/// <summary>
/// The page is four substitutions into a fixed skeleton, and three of the four come from
/// configuration - so what is worth pinning is that none of them can break out of the attribute it
/// is written into, and that the integrity attribute is present exactly when there is a hash.
/// </summary>
public class OpenApiUiPageTests {

    private static readonly OpenApiUiModel Model = new(
        "API Reference", "/openapi.json", "https://cdn.example.com/ui.js", "sha384-abc");

    private static async Task<(string Body, IExecutionContext Context)> Render(OpenApiUiModel model) {
        var request = new TestExecutionRequest(
            "GET", "/docs", "text/html",
            new SimpleQueryStringCollection(new Dictionary<string, string>()));

        var services = new ServiceCollection().BuildServiceProvider();
        var body = new MemoryStream();

        var context = new TestExecutionContext(
            services, services, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(body) { ResponseValue = model }, CancellationToken.None);

        await new OpenApiUiPage().WriteOutput(context);

        return (Encoding.UTF8.GetString(body.ToArray()), context);
    }

    [Fact]
    public async Task WriteOutput_WritesTheDocumentUrlAndScript() {
        var (page, _) = await Render(Model);

        Assert.Contains("<script id=\"api-reference\" data-url=\"/openapi.json\"></script>", page);
        Assert.Contains("src=\"https://cdn.example.com/ui.js\"", page);
        Assert.Contains("integrity=\"sha384-abc\"", page);
        Assert.Contains("crossorigin=\"anonymous\"", page);
        Assert.Contains("<title>API Reference</title>", page);
    }

    [Fact]
    public async Task WriteOutput_AnnouncesHtml() {
        var (page, context) = await Render(Model);

        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        Assert.Equal(
            Encoding.UTF8.GetByteCount(page),
            int.Parse(context.Response.Headers[KnownHeaders.ContentLength].ToString()));
    }

    /// <summary>
    /// No byte order mark. It would land ahead of the doctype, where a browser reports it as
    /// stray content.
    /// </summary>
    [Fact]
    public async Task WriteOutput_WritesNoByteOrderMark() {
        var (page, _) = await Render(Model);

        Assert.StartsWith("<!doctype html>", page);
    }

    /// <summary>
    /// An empty hash means "there is none" - which is the answer for a same-origin copy. Writing
    /// <c>integrity=""</c> instead would be a hash nothing matches, and the script would not run at
    /// all.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WriteOutput_OmitsIntegrityWhenThereIsNoneToState(string? integrity) {
        var (page, _) = await Render(Model with { ScriptIntegrity = integrity });

        Assert.DoesNotContain("integrity", page);
        Assert.DoesNotContain("crossorigin", page);
        Assert.Contains("src=\"https://cdn.example.com/ui.js\"", page);
    }

    /// <summary>
    /// Every substituted value is an attribute value or element text, so a quote in one must not be
    /// able to end the attribute it sits in.
    /// </summary>
    [Fact]
    public async Task WriteOutput_EncodesValuesSoTheyCannotBreakOutOfTheirAttribute() {
        var (page, _) = await Render(new OpenApiUiModel(
            "\"><script>alert(1)</script>",
            "/openapi.json\" onload=\"alert(1)",
            "https://cdn.example.com/ui.js\" onerror=\"alert(1)",
            "sha384-abc\" onload=\"alert(1)"));

        Assert.DoesNotContain("<script>alert(1)</script>", page);
        Assert.DoesNotContain("onload=\"alert(1)\"", page);
        Assert.DoesNotContain("onerror=\"alert(1)\"", page);
        Assert.Contains("&quot;", page);
    }

    /// <summary>
    /// The page answers HTML and only HTML: an output declares what the response <em>is</em>, and a
    /// client that will not take it gets 406 rather than the model as JSON.
    /// </summary>
    [Theory]
    [InlineData("text/html", true)]
    [InlineData("text/html,application/xhtml+xml", true)]
    [InlineData("*/*", true)]
    [InlineData(null, true)]
    [InlineData("application/json", false)]
    [InlineData("text/plain", false)]
    public void SupportsContentType_AnswersOnlyForHtml(string? accept, bool expected) {
        var request = new TestExecutionRequest(
            "GET", "/docs", accept,
            new SimpleQueryStringCollection(new Dictionary<string, string>()));

        var services = new ServiceCollection().BuildServiceProvider();

        var context = new TestExecutionContext(
            services, services, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(new MemoryStream()), CancellationToken.None);

        Assert.Equal(expected, new OpenApiUiPage().SupportsContentType(accept, context));
    }

    /// <summary>
    /// A handler returning something else is a wiring mistake, and it should say so rather than
    /// surfacing as a cast exception from inside the writer.
    /// </summary>
    [Fact]
    public async Task WriteOutput_NamesTheModelItNeededWhenGivenSomethingElse() {
        var request = new TestExecutionRequest(
            "GET", "/docs", "text/html",
            new SimpleQueryStringCollection(new Dictionary<string, string>()));

        var services = new ServiceCollection().BuildServiceProvider();

        var context = new TestExecutionContext(
            services, services, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(new MemoryStream()) { ResponseValue = "not a model" },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new OpenApiUiPage().WriteOutput(context));

        Assert.Contains(nameof(OpenApiUiModel), exception.Message);
    }

    #region the MessagePack plugin

    private static readonly OpenApiUiModel WithPlugin = Model with {
        MessagePackScriptUrl = "https://cdn.example.com/msgpack.min.js"
    };

    /// <summary>
    /// A page installing no plugin is the page it has always been: the attribute form, and no
    /// inline script anywhere on it.
    /// </summary>
    [Fact]
    public async Task WriteOutput_WithoutThePluginThereIsNoInlineScript() {
        var (page, _) = await Render(Model);

        Assert.Contains("<script id=\"api-reference\" data-url=\"/openapi.json\"></script>", page);
        Assert.DoesNotContain("createApiReference", page);
        Assert.DoesNotContain("msgpack", page);
    }

    /// <summary>
    /// A plugin is a function, so it cannot travel in a <c>data-</c> attribute. Installing one moves
    /// the page to <c>createApiReference</c>, which is the form that takes one.
    /// </summary>
    [Fact]
    public async Task WriteOutput_ThePluginFormInitialisesInScript() {
        var (page, _) = await Render(WithPlugin);

        Assert.Contains("<div id=\"app\"></div>", page);
        Assert.Contains("Scalar.createApiReference('#app', {", page);
        Assert.DoesNotContain("data-url", page);
    }

    /// <summary>The decoder, from wherever it was configured.</summary>
    [Fact]
    public async Task WriteOutput_ThePluginFormLoadsTheDecoder() {
        var (page, _) = await Render(WithPlugin);

        Assert.Contains("<script src=\"https://cdn.example.com/msgpack.min.js\"></script>", page);
    }

    /// <summary>
    /// The two spellings in the wild, and nothing else - which is what lets a page installing this
    /// keep Scalar's own rendering for every other media type.
    /// </summary>
    [Fact]
    public async Task WriteOutput_ThePluginClaimsTheMessagePackMediaTypesOnly() {
        var (page, _) = await Render(WithPlugin);

        Assert.Contains("mimeTypes: ['application/msgpack', 'application/x-msgpack']", page);
        Assert.DoesNotContain("'application/json'", page);
    }

    /// <summary>
    /// The reference UI keeps its own script tag and its hash. The plugin changes how the page
    /// initialises, not what it initialises from.
    /// </summary>
    [Fact]
    public async Task WriteOutput_ThePluginFormKeepsTheIntegrityHash() {
        var (page, _) = await Render(WithPlugin);

        Assert.Contains("src=\"https://cdn.example.com/ui.js\"", page);
        Assert.Contains("integrity=\"sha384-abc\"", page);
        Assert.Contains("crossorigin=\"anonymous\"", page);
    }

    /// <summary>
    /// The document URL now lands in a JavaScript string literal rather than an HTML attribute, so
    /// it is escaped as one. Asserted by parsing it back, which is the only check that says the
    /// value survived rather than that some escape sequence appeared.
    /// </summary>
    [Theory]
    [InlineData("/openapi.json")]
    [InlineData("/a\";globalThis.pwned=1;//")]
    [InlineData("/a\\b</script>")]
    public async Task WriteOutput_TheDocumentUrlCannotCloseTheScript(string documentPath) {
        var (page, _) = await Render(WithPlugin with { DocumentPath = documentPath });

        var start = page.IndexOf("url: ", StringComparison.Ordinal) + "url: ".Length;
        var literal = page[start..page.IndexOf(",\n", start, StringComparison.Ordinal)];

        Assert.Equal(documentPath, JsonSerializer.Deserialize<string>(literal));
    }

    #endregion
}
