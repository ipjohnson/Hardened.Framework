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
    /// The one attribute name the streaming selector accepts. Matched by simple name, so a
    /// consumer's own attribute of that name selects the streaming generator too.
    /// </summary>
    [Fact]
    public void TheStreamingModuleAttributeProducesAStreamingApplication() {
        Assert.Contains(
            "Application.StreamingApp.cs", Streaming("StreamingLambdaFunctionModule").GeneratedSources.Keys);
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
    [InlineData("StreamingLambdaFunctionModule")]
    [InlineData("StreamingLambdaFunctionModuleAttribute")]
    [InlineData("TestApp.StreamingLambdaFunctionModule")]
    [InlineData("TestApp.StreamingLambdaFunctionModuleAttribute")]
    public void EverySpellingOfTheModuleAttributeSelectsStreaming(string attribute) {
        Assert.Contains("Application.StreamingApp.cs", Streaming(attribute).GeneratedSources.Keys);
    }

    /// <summary>
    /// The three names the selector accepted until 2026-08-27, none of which can work.
    ///
    /// <para>
    /// <c>StreamingLambdaFunction</c> is what the module attribute was called before it was renamed
    /// for consistency with every other module. <c>StreamingLambdaFunctionApplication</c> and
    /// <c>LambdaFunctionApplication</c> are not types in this repository or the framework — they
    /// were string literals in the predicate and stubs in this harness, so an application written
    /// against either failed to compile on an undefined attribute.
    /// </para>
    /// <para>
    /// A retired name must not quietly keep working, and must not quietly select the other
    /// transport either: these produce the managed-runtime application, which is what an entry
    /// point that declares no streaming module should get.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("StreamingLambdaFunction")]
    [InlineData("StreamingLambdaFunctionApplication")]
    [InlineData("LambdaFunctionApplication")]
    public void ARetiredAttributeNameDoesNotProduceAStreamingApplication(string attribute) {
        var result = FunctionGeneratorHarness.RunBoth(
            FunctionGeneratorHarness.Application(attributes: $"[{attribute}]"),
            FunctionGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();

        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// A streaming function is its own host: the emitted <c>Main</c> builds the application, resolves
    /// the invoke engine from its container and hands control to it. Nothing else starts the poll
    /// loop, so an application missing this entry point starts and immediately exits.
    /// </summary>
    [Fact]
    public void TheStreamingApplicationHostsItselfThroughAGeneratedMain() {
        var application = Streaming("StreamingLambdaFunctionModule").SourceContaining("StreamingApp");

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
        var application = Streaming("StreamingLambdaFunctionModule").SourceContaining("StreamingApp");

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
        var application = Streaming("StreamingLambdaFunctionModule").SourceContaining("StreamingApp");

        Assert.DoesNotContain("ILambdaFunctionImplService", application);
        Assert.DoesNotContain("InvokeFunction", application);
    }

    /// <summary>
    /// The streaming selector requires <c>[HardenedModule]</c>, as the managed-runtime one always
    /// has. Changed 2026-08-27; it previously treated the streaming attribute alone as claiming an
    /// entry point, so a class that was not an application — a module declaration, a helper that
    /// happened to carry the attribute — had a <c>Main</c> and a Lambda poll loop generated onto it.
    /// </summary>
    [Fact]
    public void AStreamingModuleOnANonEntryPointDoesNotProduceAnApplication() {
        var withoutModule = FunctionGeneratorHarness.Application(attributes: "[StreamingLambdaFunctionModule]")
            .Replace("[HardenedModule]", "");

        var result = FunctionGeneratorHarness.Generate(
            new StreamingFunctionLambdaSourceGenerator(),
            withoutModule,
            FunctionGeneratorHarness.StreamingAttributes);

        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The two generators are exclusive. An entry point carrying a streaming attribute gets the
    /// streaming application and no managed-runtime one, even with both generators running.
    /// </summary>
    [Fact]
    public void AStreamingEntryPointIsNotAlsoGivenAManagedRuntimeApplication() {
        var result = FunctionGeneratorHarness.RunBoth(
            FunctionGeneratorHarness.Application(attributes: "[StreamingLambdaFunctionModule]"),
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
    /// Recorded 2026-08-12, fixed 2026-08-27. The selectors called <c>IsAttributed</c>, which
    /// searches <c>DescendantNodes()</c> — every node inside the class, not only the attributes on
    /// it. A streaming attribute written on a <em>member</em> therefore claimed the whole class for
    /// the streaming generator and excluded it from the managed-runtime one, giving the entry point
    /// a <c>Main</c> and a poll loop the consumer never asked for.
    ///
    /// <para>
    /// The selectors now read the class's own <c>AttributeLists</c>, so what decides the transport
    /// is what is written on the application.
    /// </para>
    /// </summary>
    [Fact]
    public void AStreamingAttributeOnAMemberDoesNotClaimTheClassForTheStreamingGenerator() {
        var result = FunctionGeneratorHarness.RunBoth(
            FunctionGeneratorHarness.Application("""
                    [StreamingLambdaFunctionModule]
                    public void Unrelated() { }
                """),
            FunctionGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// The nested-type half of the same defect: an attribute on a nested class used to select the
    /// enclosing application <em>and</em> generate a second streaming host for the nested type.
    /// </summary>
    [Fact]
    public void AStreamingAttributeOnANestedTypeDoesNotClaimTheEnclosingClass() {
        var result = FunctionGeneratorHarness.RunBoth(
            FunctionGeneratorHarness.Application("""
                    [StreamingLambdaFunctionModule]
                    public partial class Inner { }
                """),
            FunctionGeneratorHarness.StreamingAttributes);

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Inner.StreamingApp.cs", result.GeneratedSources.Keys);
    }
}
