using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// <see cref="StreamingFunctionLambdaSourceGenerator"/> — the custom-runtime form of a function
/// application.
///
/// <para>
/// A streaming function is not invoked by the AWS managed runtime through a handler method; it is an
/// executable that polls the Lambda runtime API itself. So the emitted application has a <c>Main</c>
/// instead of an <c>Invoke</c>, and no implementation service field.
/// </para>
///
/// <para>
/// The two function generators are selected between by attribute, and their selectors are written as
/// each other's negation. That is the interesting behaviour here: which generator claims an entry
/// point, and what happens when neither or both could.
/// </para>
/// </summary>
public class StreamingFunctionGeneratorTests {

    private static Hardened.Amz.SourceGeneration.Testing.GeneratorResult Streaming(string attribute) =>
        FunctionGeneratorHarness.Generate(
            new StreamingFunctionLambdaSourceGenerator(),
            FunctionGeneratorHarness.Application(attributes: $"[{attribute}]"),
            FunctionGeneratorHarness.StreamingAttributes);

    /// <summary>
    /// The three attribute names the streaming selector accepts. All three are matched by name only,
    /// so a consumer's own attribute of the same simple name selects the streaming generator too.
    /// </summary>
    [Theory]
    [InlineData("StreamingLambdaFunction")]
    [InlineData("StreamingLambdaFunctionApplication")]
    [InlineData("LambdaFunctionApplication")]
    public void EveryAttributeTheStreamingSelectorAcceptsProducesAStreamingApplication(string attribute) {
        var result = Streaming(attribute);

        Assert.Contains("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// A streaming function is its own host: the emitted <c>Main</c> builds the application, resolves
    /// the invoke engine from its container and hands control to it. Nothing else starts the poll
    /// loop, so an application missing this entry point starts and immediately exits.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationHostsItselfThroughAGeneratedMain() {
        var application = Streaming("StreamingLambdaFunction").SourceContaining("StreamingApp");

        Assert.Contains(
            "public static async global::System.Threading.Tasks.Task Main(string[] args)", application);
        FunctionGeneratorHarness.AssertEmits(application, "var app = new global::TestApp.Application();");
        FunctionGeneratorHarness.AssertEmits(application,
            "var engine = app.RootServiceProvider.GetRequiredService<" +
            "global::Hardened.Amz.Function.Lambda.Streaming.Impl.IFunctionInvokeEngine>();");
        FunctionGeneratorHarness.AssertEmits(application,
            "await engine.InvokeAsync(System.Threading.CancellationToken.None);");
    }

    /// <summary>
    /// The invoke filter chain is wired into the middleware pipeline exactly as it is for a managed
    /// runtime function — the transport differs, the request pipeline does not.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationStillWiresTheInvokeFilterIntoTheMiddleware() {
        var application = Streaming("StreamingLambdaFunction").SourceContaining("StreamingApp");

        FunctionGeneratorHarness.AssertEmits(application,
            "var handler = filterProvider.ProvideFilter(RootServiceProvider);");
        FunctionGeneratorHarness.AssertEmits(application, "middleware.Use(_ => handler);");
    }

    /// <summary>
    /// A streaming application has no <c>ILambdaFunctionImplService</c> and no stream-in/stream-out
    /// <c>Invoke</c>: those belong to the managed runtime path, and emitting them here would resolve
    /// a service the streaming module never registers.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationHasNoManagedRuntimeInvokePath() {
        var application = Streaming("StreamingLambdaFunction").SourceContaining("StreamingApp");

        Assert.DoesNotContain("ILambdaFunctionImplService", application);
        Assert.DoesNotContain("InvokeFunction", application);
    }

    /// <summary>
    /// The streaming selector does not require <c>[HardenedModule]</c> — the attribute alone claims
    /// the entry point. The non-streaming generator does require it, so this source is claimed by
    /// exactly one of the two.
    /// </summary>
    [Fact]
    public void TheStreamingAttributeAloneIsEnoughToClaimAnEntryPoint() {
        var withoutModule = FunctionGeneratorHarness.Application(attributes: "[StreamingLambdaFunction]")
            .Replace("[HardenedModule]", "");

        var result = FunctionGeneratorHarness.Generate(
            new StreamingFunctionLambdaSourceGenerator(),
            withoutModule,
            FunctionGeneratorHarness.StreamingAttributes);

        Assert.Contains("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The two generators are exclusive. An entry point carrying a streaming attribute gets the
    /// streaming application and no managed-runtime one, even with both generators running.
    /// </summary>
    [Fact]
    public void AStreamingEntryPointIsNotAlsoGivenAManagedRuntimeApplication() {
        var result = FunctionGeneratorHarness.RunBoth(
            FunctionGeneratorHarness.Application(attributes: "[StreamingLambdaFunction]"),
            FunctionGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.StreamingApp.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.Empty(result.DuplicateHintNames);
    }

    /// <summary>
    /// A plain <c>[HardenedModule]</c> entry point is claimed by the managed runtime generator only.
    /// </summary>
    [Fact]
    public void AnEntryPointWithoutAStreamingAttributeIsNotGivenAStreamingApplication() {
        var result = FunctionGeneratorHarness.RunBoth(FunctionGeneratorHarness.Application());

        result.AssertNoErrors();

        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// Recorded 2026-08-12. The selectors call <c>IsAttributed</c>, which searches
    /// <c>DescendantNodes()</c> — every node inside the class, not only the attributes on it. A
    /// streaming attribute written on a <em>member</em> therefore excludes the whole class from the
    /// managed runtime generator while still not being an attribute on the class, so neither
    /// generator's positive test is what decides it.
    ///
    /// <para>
    /// The observable consequence is asserted rather than assumed: this entry point gets a streaming
    /// application, no managed-runtime one, and a <c>Main</c> the consumer did not ask for.
    /// </para>
    /// </summary>
    [Fact]
    public void AStreamingAttributeOnAMemberClaimsTheWholeClassForTheStreamingGenerator() {
        var result = FunctionGeneratorHarness.RunBoth(
            FunctionGeneratorHarness.Application("""
                    [StreamingLambdaFunction]
                    public void Unrelated() { }
                """),
            FunctionGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.StreamingApp.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
    }
}
