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
    /// The two attribute names the streaming selector accepts. Both are matched by simple name, so a
    /// consumer's own attribute of the same name selects the streaming generator too.
    /// </summary>
    [Theory]
    [InlineData("StreamingLambdaWebApplication")]
    [InlineData("StreamingLambdaWebModule")]
    public void EitherAttributeTheStreamingSelectorAcceptsProducesAStreamingApplication(string attribute) {
        Assert.Contains("Application.StreamingApp.cs", Streaming(attribute).GeneratedSources.Keys);
    }

    /// <summary>
    /// A streaming web application hosts itself: the emitted <c>Main</c> builds the application,
    /// resolves the invoke engine from its container and hands control to it. Nothing else starts the
    /// poll loop.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationHostsItselfThroughAGeneratedMain() {
        var application = Streaming("StreamingLambdaWebApplication").SourceContaining("StreamingApp");

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
        var application = Streaming("StreamingLambdaWebApplication").SourceContaining("StreamingApp");

        WebGeneratorHarness.AssertEmits(application,
            "var handler = RootServiceProvider.GetRequiredService<" +
            "global::Hardened.Web.Runtime.Handlers.IWebExecutionHandlerService>();");
        WebGeneratorHarness.AssertEmits(application, "middleware.Use(_ => handler);");
    }

    /// <summary>
    /// A streaming application has no API Gateway event processor and no proxy-shaped handler: those
    /// belong to the managed integration, and emitting them would resolve a service the streaming
    /// module never registers.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationHasNoApiGatewayInvokePath() {
        var application = Streaming("StreamingLambdaWebApplication").SourceContaining("StreamingApp");

        Assert.DoesNotContain("IApiGatewayEventProcessor", application);
        Assert.DoesNotContain("APIGatewayHttpApiV2ProxyRequest", application);
        Assert.DoesNotContain("LambdaSerializer", application);
    }

    /// <summary>
    /// The streaming selector does not require <c>[HardenedModule]</c> — the attribute alone claims
    /// the entry point.
    /// </summary>
    [Fact]
    public void TheStreamingAttributeAloneIsEnoughToClaimAnEntryPoint() {
        var result = WebGeneratorHarness.Generate(
            new StreamingWebLambdaSourceGenerator(),
            WebGeneratorHarness.Application(attributes: "[StreamingLambdaWebApplication]")
                .Replace("[HardenedModule]", ""),
            WebGeneratorHarness.StreamingAttributes);

        Assert.Contains("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The two generators are exclusive. An entry point carrying a streaming attribute gets the
    /// streaming application and no API Gateway one, even with both generators running.
    /// </summary>
    [Fact]
    public void AStreamingEntryPointIsNotAlsoGivenAnApiGatewayApplication() {
        var result = WebGeneratorHarness.RunBoth(
            WebGeneratorHarness.Application(attributes: "[StreamingLambdaWebApplication]"),
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
    /// Recorded 2026-08-12. The selectors call <c>IsAttributed</c>, which searches
    /// <c>DescendantNodes()</c> — every node inside the class, not only the attributes on it. A
    /// streaming attribute written on a <em>member</em> therefore excludes the whole class from the
    /// API Gateway generator, and the streaming one claims it.
    ///
    /// <para>
    /// The observable consequence is asserted rather than assumed: this entry point gets a streaming
    /// application, no API Gateway one, and a <c>Main</c> the consumer did not ask for.
    /// </para>
    /// </summary>
    [Fact]
    public void AStreamingAttributeOnAMemberClaimsTheWholeClassForTheStreamingGenerator() {
        var result = WebGeneratorHarness.RunBoth(
            WebGeneratorHarness.Application("""
                    [StreamingLambdaWebApplication]
                    public void Unrelated() { }
                """),
            WebGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();
        WebGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.StreamingApp.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.App.cs", result.GeneratedSources.Keys);
    }
}
