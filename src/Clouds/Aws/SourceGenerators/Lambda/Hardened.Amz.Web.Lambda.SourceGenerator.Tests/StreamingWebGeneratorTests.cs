using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// <see cref="StreamingWebLambdaSourceGenerator"/> — the custom-runtime form of a web application.
///
/// <para>
/// A streaming web application is not invoked by API Gateway through a handler method; it is an
/// executable that polls the Lambda runtime API and writes its response to a stream. So the emitted
/// application has a <c>Main</c> instead of an <c>Invoke</c>, no event processor, and no serialiser
/// attribute.
/// </para>
///
/// <para>
/// The two web generators are selected between by attribute, and their selectors are written as each
/// other's negation. That is the interesting behaviour here: which generator claims an entry point.
/// </para>
/// </summary>
public class StreamingWebGeneratorTests {

    private static Hardened.Amz.SourceGeneration.Testing.GeneratorResult Streaming(string attribute) =>
        WebGeneratorHarness.Generate(
            new StreamingWebLambdaSourceGenerator(),
            WebGeneratorHarness.Application(attributes: $"[{attribute}]"),
            WebGeneratorHarness.StreamingAttributes);

    /// <summary>
    /// The one attribute name the streaming selector accepts. Matched by simple name, so a
    /// consumer's own attribute of that name selects the streaming generator too.
    /// </summary>
    [Fact]
    public void TheStreamingModuleAttributeProducesAStreamingApplication() {
        Assert.Contains(
            "Application.StreamingApp.cs", Streaming("StreamingLambdaWebModule").GeneratedSources.Keys);
    }

    /// <summary>
    /// The spellings C# allows for one attribute, all of which select streaming.
    ///
    /// <para>
    /// The selector compares simple names in syntax and never resolves a symbol, so each of these
    /// has to be handled by the comparison itself: the suffix C# lets you omit, and a qualified
    /// name where only the last segment is the attribute. A selector that missed one would emit the
    /// other transport for source that plainly asks for this one.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("StreamingLambdaWebModule")]
    [InlineData("StreamingLambdaWebModuleAttribute")]
    [InlineData("TestApp.StreamingLambdaWebModule")]
    [InlineData("TestApp.StreamingLambdaWebModuleAttribute")]
    public void EverySpellingOfTheModuleAttributeSelectsStreaming(string attribute) {
        Assert.Contains("Application.StreamingApp.cs", Streaming(attribute).GeneratedSources.Keys);
    }

    /// <summary>
    /// <c>[StreamingLambdaWebApplication]</c> stopped selecting the streaming generator on
    /// 2026-08-27. It registers no services — an application selected by it got a streaming
    /// bootstrap over an empty container and threw on construction — and it is
    /// <c>[Obsolete(error: true)]</c> in the runtime now, so no source carrying it compiles at all.
    ///
    /// <para>
    /// Asserted here because the failure to avoid is the quiet one: the name must not keep
    /// selecting streaming, and an entry point that declares no streaming module must get the API
    /// Gateway application rather than nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRetiredApplicationAttributeDoesNotProduceAStreamingApplication() {
        var result = WebGeneratorHarness.RunBoth(
            WebGeneratorHarness.Application(attributes: "[StreamingLambdaWebApplication]"),
            WebGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();

        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// A streaming web application hosts itself: the emitted <c>Main</c> builds the application,
    /// resolves the invoke engine from its container and hands control to it. Nothing else starts the
    /// poll loop.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationHostsItselfThroughAGeneratedMain() {
        var application = Streaming("StreamingLambdaWebModule").SourceContaining("StreamingApp");

        Assert.Contains(
            "public static async global::System.Threading.Tasks.Task Main(string[] args)", application);
        WebGeneratorHarness.AssertEmits(application, "var app = new global::TestApp.Application();");
        WebGeneratorHarness.AssertEmits(application,
            "var engine = app.RootServiceProvider.GetRequiredService<" +
            "global::Hardened.Amz.Web.Lambda.Streaming.Impl.ILambdaInvokeEngine>();");
        WebGeneratorHarness.AssertEmits(application,
            "await engine.InvokeAsync(System.Threading.CancellationToken.None);");
    }

    /// <summary>
    /// The web execution handler is wired into the middleware pipeline exactly as it is behind API
    /// Gateway — the transport differs, the request pipeline does not.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationStillWiresTheWebHandlerIntoTheMiddleware() {
        var application = Streaming("StreamingLambdaWebModule").SourceContaining("StreamingApp");

        WebGeneratorHarness.AssertEmits(application,
            "var handler = global::Hardened.Amz.Shared.Lambda.Runtime.ApplicationServices.Require<" +
            "global::Hardened.Web.Runtime.Handlers.IWebExecutionHandlerService>(" +
            "RootServiceProvider, \"Application\", \"StreamingLambdaWebModule\");");
        WebGeneratorHarness.AssertEmits(application, "middleware.Use(_ => handler);");
    }

    /// <summary>
    /// A streaming application has no API Gateway event processor and no proxy-shaped handler: those
    /// belong to the managed integration, and emitting them would resolve a service the streaming
    /// module never registers.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationHasNoApiGatewayInvokePath() {
        var application = Streaming("StreamingLambdaWebModule").SourceContaining("StreamingApp");

        Assert.DoesNotContain("IApiGatewayEventProcessor", application);
        Assert.DoesNotContain("APIGatewayHttpApiV2ProxyRequest", application);
        Assert.DoesNotContain("LambdaSerializer", application);
    }

    /// <summary>
    /// The streaming selector requires <c>[HardenedModule]</c>, as the API Gateway one always has.
    /// Changed 2026-08-27; it previously treated the streaming attribute alone as claiming an entry
    /// point, so a class that was not an application had a <c>Main</c> and a Lambda poll loop
    /// generated onto it.
    /// </summary>
    [Fact]
    public void AStreamingModuleOnANonEntryPointDoesNotProduceAnApplication() {
        var result = WebGeneratorHarness.Generate(
            new StreamingWebLambdaSourceGenerator(),
            WebGeneratorHarness.Application(attributes: "[StreamingLambdaWebModule]")
                .Replace("[HardenedModule]", ""),
            WebGeneratorHarness.StreamingAttributes);

        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The two generators are exclusive. An entry point carrying a streaming attribute gets the
    /// streaming application and no API Gateway one, even with both generators running.
    /// </summary>
    [Fact]
    public void AStreamingEntryPointIsNotAlsoGivenAnApiGatewayApplication() {
        var result = WebGeneratorHarness.RunBoth(
            WebGeneratorHarness.Application(attributes: "[StreamingLambdaWebModule]"),
            WebGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();
        WebGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.StreamingApp.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.App.cs", result.GeneratedSources.Keys);
        Assert.Empty(result.DuplicateHintNames);
    }

    /// <summary>
    /// A plain <c>[HardenedModule]</c> entry point is claimed by the API Gateway generator only.
    /// </summary>
    [Fact]
    public void AnEntryPointWithoutAStreamingAttributeIsNotGivenAStreamingApplication() {
        var result = WebGeneratorHarness.RunBoth(WebGeneratorHarness.Application());

        result.AssertNoErrors();

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// Recorded 2026-08-12, fixed 2026-08-27. The selectors called <c>IsAttributed</c>, which
    /// searches <c>DescendantNodes()</c> — every node inside the class, not only the attributes on
    /// it. A streaming attribute written on a <em>member</em> therefore claimed the whole class for
    /// the streaming generator and excluded it from the API Gateway one, giving the entry point a
    /// <c>Main</c> and a poll loop the consumer never asked for.
    ///
    /// <para>
    /// The selectors now read the class's own <c>AttributeLists</c>, so what decides the transport
    /// is what is written on the application.
    /// </para>
    /// </summary>
    [Fact]
    public void AStreamingAttributeOnAMemberDoesNotClaimTheClassForTheStreamingGenerator() {
        var result = WebGeneratorHarness.RunBoth(
            WebGeneratorHarness.Application("""
                    [StreamingLambdaWebModule]
                    public void Unrelated() { }
                """),
            WebGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();
        WebGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The nested-type half of the same defect: an attribute on a nested class used to select the
    /// enclosing application <em>and</em> generate a second streaming host for the nested type.
    /// </summary>
    [Fact]
    public void AStreamingAttributeOnANestedTypeDoesNotClaimTheEnclosingClass() {
        var result = WebGeneratorHarness.RunBoth(
            WebGeneratorHarness.Application("""
                    [StreamingLambdaWebModule]
                    public partial class Inner { }
                """),
            WebGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();
        WebGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Inner.StreamingApp.cs", result.GeneratedSources.Keys);
    }
}
