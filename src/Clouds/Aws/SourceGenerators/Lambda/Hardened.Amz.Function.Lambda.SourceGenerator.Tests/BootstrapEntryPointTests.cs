using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// The executable half of <c>Application.LambdaApplication.cs</c>: a <c>Main</c> that runs the
/// application on <c>Amazon.Lambda.RuntimeSupport</c>'s bootstrap.
///
/// <para>
/// Until 2026-09-04 only an entry point carrying <c>[StreamingLambdaFunctionModule]</c> got a
/// <c>Main</c>, and it drove the Lambda Runtime API through a hand-rolled host that never reported
/// an error. Every entry point gets one now, on the AWS bootstrap, with the same <c>Invoke</c>
/// the managed runtime calls as its handler.
/// </para>
/// </summary>
public class BootstrapEntryPointTests {

    private static string Application(string members = "", string attributes = "") =>
        FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application(members, attributes))
            .SourceContaining("LambdaApplication");

    [Fact]
    public void TheApplicationHostsItselfThroughAGeneratedMain() {
        var application = Application();

        Assert.Contains(
            "public static async global::System.Threading.Tasks.Task Main(string[] args)", application);
        FunctionGeneratorHarness.AssertEmits(application, "var app = new global::TestApp.Application();");
    }

    /// <summary>
    /// The bootstrap is built on the application's own <c>Invoke</c>, cast to the raw stream
    /// delegate so the right one of the builder's many overloads is chosen.
    /// </summary>
    [Fact]
    public void MainHandsInvokeToTheBootstrapOnTheRawStreamOverload() {
        var application = Application();

        FunctionGeneratorHarness.AssertEmits(application,
            "using var bootstrap = global::Amazon.Lambda.RuntimeSupport.LambdaBootstrapBuilder.Create(" +
            "(global::System.Func<global::System.IO.Stream, global::Amazon.Lambda.Core.ILambdaContext, " +
            "global::System.Threading.Tasks.Task<global::System.IO.Stream>>)app.Invoke).Build();");
        FunctionGeneratorHarness.AssertEmits(application,
            "await bootstrap.RunAsync(global::System.Threading.CancellationToken.None);");
    }

    /// <summary>
    /// There is one generator and one pair of files per entry point. An attribute that used to
    /// select the streaming host - a consumer's own attribute of that name, since the runtime's is
    /// gone - now selects nothing.
    /// </summary>
    [Theory]
    [InlineData("StreamingLambdaFunctionModule")]
    [InlineData("StreamingLambdaFunctionModuleAttribute")]
    [InlineData("TestApp.StreamingLambdaFunctionModule")]
    public void AStreamingModuleAttributeNoLongerSelectsASecondHost(string attribute) {
        var result = FunctionGeneratorHarness.Generate(
            FunctionGeneratorHarness.Application(attributes: $"[{attribute}]"),
            """
            namespace TestApp;

            public class StreamingLambdaFunctionModuleAttribute : System.Attribute { }
            """);

        Assert.Equal(2, result.GeneratedSources.Count);
        Assert.Contains("Application.LambdaApplication.cs", result.GeneratedSources.Keys);
        Assert.Contains("Application.LambdaHandlerPackage.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }
}
