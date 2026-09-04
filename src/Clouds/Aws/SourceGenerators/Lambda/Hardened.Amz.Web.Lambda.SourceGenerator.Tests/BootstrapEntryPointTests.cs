using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// The executable half of <c>Application.App.cs</c>: a <c>Main</c> that runs the application on
/// <c>Amazon.Lambda.RuntimeSupport</c>'s bootstrap.
///
/// <para>
/// Until 2026-09-04 only an entry point carrying <c>[StreamingLambdaWebModule]</c> got a
/// <c>Main</c>, and it drove the Lambda Runtime API through a hand-rolled host. Every entry point
/// gets one now, the host is the AWS bootstrap, and whether a response streams is a deployment
/// setting the running application reads. The buffered <c>Invoke</c> stays beside it for the
/// managed runtime's class-library handler shape, the tests and the local harness.
/// </para>
/// </summary>
public class BootstrapEntryPointTests {

    private static string Application(string members = "", string attributes = "") =>
        WebGeneratorHarness.Generate(WebGeneratorHarness.Application(members, attributes)).SourceContaining("App");

    [Fact]
    public void TheApplicationHostsItselfThroughAGeneratedMain() {
        var application = Application();

        Assert.Contains(
            "public static async global::System.Threading.Tasks.Task Main(string[] args)", application);
        WebGeneratorHarness.AssertEmits(application, "var app = new global::TestApp.Application();");
        WebGeneratorHarness.AssertEmits(application,
            "var host = app.RootServiceProvider.GetRequiredService<" +
            "global::Hardened.Amz.Web.Lambda.Runtime.Impl.ILambdaWebHost>();");
    }

    /// <summary>
    /// The raw stream overload, selected by casting the method group. It is the shape the bootstrap
    /// offers for custom serializers and Native AOT, and the host reads the event off the stream
    /// itself.
    /// </summary>
    [Fact]
    public void MainHandsTheHostToTheBootstrapOnTheRawStreamOverload() {
        var application = Application();

        WebGeneratorHarness.AssertEmits(application,
            "using var bootstrap = global::Amazon.Lambda.RuntimeSupport.LambdaBootstrapBuilder.Create(" +
            "(global::System.Func<global::System.IO.Stream, global::Amazon.Lambda.Core.ILambdaContext, " +
            "global::System.Threading.Tasks.Task<global::System.IO.Stream>>)host.Invoke).Build();");
        WebGeneratorHarness.AssertEmits(application,
            "await bootstrap.RunAsync(global::System.Threading.CancellationToken.None);");
    }

    /// <summary>
    /// The buffered handler is still there: a class library deployed with a
    /// <c>Assembly::Type::Invoke</c> handler keeps working, and the tests that drive the generated
    /// class directly keep compiling.
    /// </summary>
    [Fact]
    public void TheBufferedInvokeMethodIsEmittedBesideMain() {
        var application = Application();

        Assert.Contains(
            "public global::System.Threading.Tasks.Task<" +
            "global::Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse> Invoke(",
            application);
        Assert.Contains("Task Main(string[] args)", application);
    }

    /// <summary>
    /// There is one generator and one output per entry point. An attribute that used to select the
    /// streaming host - a consumer's own attribute of that name, since the runtime's is gone - now
    /// selects nothing, and the entry point gets the one application every other entry point gets.
    /// </summary>
    [Theory]
    [InlineData("StreamingLambdaWebModule")]
    [InlineData("StreamingLambdaWebModuleAttribute")]
    [InlineData("TestApp.StreamingLambdaWebModule")]
    public void AStreamingModuleAttributeNoLongerSelectsASecondHost(string attribute) {
        var result = WebGeneratorHarness.Generate(
            WebGeneratorHarness.Application(attributes: $"[{attribute}]"),
            """
            namespace TestApp;

            public class StreamingLambdaWebModuleAttribute : System.Attribute { }
            """);

        Assert.Single(result.GeneratedSources.Keys);
        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.DoesNotContain("Application.StreamingApp.cs", result.GeneratedSources.Keys);
    }
}
