using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// <c>Application.App.cs</c> — the class API Gateway's Lambda integration invokes.
///
/// <para>
/// <see cref="LambdaWebApplicationFileWriter"/> derives from the framework's
/// <c>ApplicationEntryPointFileWriter</c> and adds the three things that make an application an API
/// Gateway handler: the web execution handler wired into the middleware pipeline, the event
/// processor resolved into a field, and an <c>Invoke</c> method taking a proxy request.
/// </para>
/// </summary>
public class ApiGatewayEntryPointTests {

    private static string Application(string members = "") =>
        WebGeneratorHarness.Generate(WebGeneratorHarness.Application(members)).SourceContaining("App");

    /// <summary>
    /// The handler signature API Gateway binds to: a v2 proxy request in, a v2 proxy response out,
    /// with the invocation context alongside.
    /// </summary>
    [Fact]
    public void TheApplicationExposesAProxyRequestInProxyResponseOutInvokeMethod() {
        Assert.Contains(
            "public global::System.Threading.Tasks.Task<" +
            "global::Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse> Invoke(" +
            "global::Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyRequest request, " +
            "global::Amazon.Lambda.Core.ILambdaContext context)",
            Application());
    }

    /// <summary>
    /// The handler is annotated with the serialiser AWS uses to bind the event JSON. Without it the
    /// runtime cannot deserialise an API Gateway event into the request parameter at all.
    /// </summary>
    [Fact]
    public void TheHandlerDeclaresTheLambdaJsonSerialiser() {
        Assert.Contains(
            "[LambdaSerializer(typeof(" +
            "Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]",
            Application());
    }

    /// <summary>
    /// The application implements the runtime's handler interface, so a harness or a test can hold
    /// one without knowing the generated type.
    /// </summary>
    [Fact]
    public void TheApplicationImplementsTheRuntimesApiGatewayHandlerInterface() {
        Assert.Contains(
            "global::Hardened.Amz.Web.Lambda.Runtime.Impl.IApiGatewayV2Handler", Application());
    }

    /// <summary>
    /// <c>Invoke</c> delegates straight to the event processor, passing both of its arguments
    /// through. The context carries the request id and the remaining time, so dropping it loses both.
    /// </summary>
    [Fact]
    public void InvokeDelegatesBothArgumentsToTheEventProcessor() {
        WebGeneratorHarness.AssertEmits(Application(), "return _eventProcessor.Process(request, context);");
    }

    /// <summary>
    /// The event processor is resolved once in the constructor and held in a field, rather than
    /// looked up per request.
    /// </summary>
    [Fact]
    public void TheEventProcessorIsResolvedOnceIntoAField() {
        var application = Application();

        Assert.Contains(
            "private global::Hardened.Amz.Web.Lambda.Runtime.Impl.IApiGatewayEventProcessor _eventProcessor;",
            application);

        WebGeneratorHarness.AssertEmits(application,
            "_eventProcessor = RootServiceProvider.GetRequiredService<" +
            "global::Hardened.Amz.Web.Lambda.Runtime.Impl.IApiGatewayEventProcessor>();");
    }

    /// <summary>
    /// The web execution handler is resolved once and registered as the terminal middleware. Both
    /// halves matter: a handler resolved but never <c>Use</c>d leaves the pipeline with nothing to
    /// run, and every request 404s.
    /// </summary>
    [Fact]
    public void TheWebExecutionHandlerIsResolvedOnceAndRegisteredAsMiddleware() {
        var application = Application();

        WebGeneratorHarness.AssertEmits(application,
            "var handler = global::Hardened.Amz.Shared.Lambda.Runtime.ApplicationServices.Require<" +
            "global::Hardened.Web.Runtime.Handlers.IWebExecutionHandlerService>(" +
            "RootServiceProvider, \"Application\", \"LambdaWebModule\");");
        WebGeneratorHarness.AssertEmits(application, "middleware.Use(_ => handler);");
    }

    /// <summary>
    /// The application is an <c>IApplicationRoot</c>, so the host can reach its container and dispose
    /// it. Both members of that contract are emitted.
    /// </summary>
    [Fact]
    public void TheApplicationIsAnApplicationRootWithAProviderAndAsynchronousDisposal() {
        var application = Application();

        Assert.Contains("global::Hardened.Shared.Runtime.Application.IApplicationRoot", application);
        Assert.Contains("RootServiceProvider ?? throw new Exception", application);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask DisposeAsync()", application);
    }

    /// <summary>
    /// Disposal reads the field into a local before clearing it, so a second <c>DisposeAsync</c>
    /// finds the field already null and skips the dispose rather than disposing twice.
    /// </summary>
    [Fact]
    public void DisposalCapturesTheProviderBeforeClearingTheField() {
        var application = Application();

        var capture = application.IndexOf(
            "currentRootServiceProvider = RootServiceProvider", StringComparison.Ordinal);
        var clear = application.IndexOf("RootServiceProvider = null", StringComparison.Ordinal);

        Assert.True(capture >= 0, "the provider is never captured before being cleared");
        Assert.True(clear > capture, "the field is cleared before it is captured, so nothing is disposed");
        Assert.Contains("if (RootServiceProvider != null)", application);
    }

    /// <summary>
    /// The parameterless constructor is what the Lambda runtime calls when it instantiates the
    /// handler type. It chains to the environment overload with a default environment and no
    /// dependency overrides; without it the function cannot start at all.
    /// </summary>
    [Fact]
    public void TheParameterlessConstructorSuppliesADefaultEnvironment() {
        WebGeneratorHarness.AssertEmits(Application(),
            ": this(new global::Hardened.Shared.Runtime.Application.EnvironmentImpl(), null)");
    }

    /// <summary>
    /// <c>RegisterInitDi</c> is emitted whether or not anything fills it in — it is the seam module
    /// generators write registrations into, and the constructor already passes it by name.
    /// </summary>
    [Fact]
    public void TheRegisterInitDiSeamIsAlwaysEmitted() {
        Assert.Contains("private static void RegisterInitDi(", Application());
    }

    /// <summary>
    /// Recorded 2026-08-12. The Lambda web generator emits no dependency registration of its own —
    /// no <c>DependencyRegistry</c> field and no DI method. Everything the application resolves is
    /// registered by <c>LambdaWebModule</c>, the hand-written <c>[DependencyModule]</c> in
    /// <c>Hardened.Amz.Web.Lambda.Runtime</c>, which the consumer brings in by attribute.
    ///
    /// <para>
    /// This is the difference from the function generator, whose handler package is a generated type
    /// and therefore has to register itself. Asserted so that adding a registration here is a
    /// deliberate change rather than an accident — two registrations of the same service from a
    /// generated partial and a module would double-register it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheApplicationCarriesNoGeneratedDependencyRegistrationOfItsOwn() {
        var application = Application();

        Assert.DoesNotContain("DependencyRegistry<", application);
        Assert.DoesNotContain("AddSingleton", application);
    }
}
