using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// <c>Application.LambdaApplication.cs</c> — the class AWS Lambda actually invokes.
///
/// <para>
/// <see cref="LambdaApplicationEntryPointWriter"/> derives from the framework's
/// <c>ApplicationEntryPointFileWriter</c> and adds the two things that make an application a Lambda
/// function: the invoke filter chain resolved into the middleware pipeline at construction, and an
/// <c>Invoke</c> method taking the raw request stream. Everything here is checked against a
/// compilation that includes the emitted file.
/// </para>
/// </summary>
public class LambdaApplicationEntryPointTests {

    private static string Application(string members = "") =>
        FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application(members))
            .SourceContaining("LambdaApplication");

    /// <summary>
    /// The handler signature the Lambda runtime binds to: a request stream in, a response stream
    /// out, with the invocation context alongside. This is the whole point of the file.
    /// </summary>
    [Fact]
    public void TheApplicationExposesAStreamInStreamOutInvokeMethod() {
        var application = Application();

        Assert.Contains(
            "public global::System.Threading.Tasks.Task<global::System.IO.Stream> Invoke(" +
            "global::System.IO.Stream inputStream, global::Amazon.Lambda.Core.ILambdaContext lambdaContext)",
            application);
    }

    /// <summary>
    /// <c>Invoke</c> delegates straight to the runtime's implementation service rather than doing
    /// any work itself, passing both of its arguments through.
    /// </summary>
    [Fact]
    public void InvokeDelegatesBothArgumentsToTheFunctionImplementationService() {
        FunctionGeneratorHarness.AssertEmits(
            Application(), "return _lambdaFunctionImplService.InvokeFunction(inputStream, lambdaContext);");
    }

    /// <summary>
    /// The implementation service is resolved once in the constructor and held in a field. Resolving
    /// it per invocation would put a container lookup on every request.
    /// </summary>
    [Fact]
    public void TheFunctionImplementationServiceIsResolvedOnceIntoAField() {
        var application = Application();

        Assert.Contains(
            "private global::Hardened.Amz.Function.Lambda.Runtime.Impl.ILambdaFunctionImplService " +
            "_lambdaFunctionImplService;",
            application);

        FunctionGeneratorHarness.AssertEmits(application,
            "_lambdaFunctionImplService = RootServiceProvider.GetRequiredService<" +
            "global::Hardened.Amz.Function.Lambda.Runtime.Impl.ILambdaFunctionImplService>();");
    }

    /// <summary>
    /// The invoke filter chain is built once at construction and registered as the terminal
    /// middleware. Both halves matter: a provider resolved but never <c>Use</c>d leaves the pipeline
    /// with nothing to run.
    /// </summary>
    [Fact]
    public void TheInvokeFilterChainIsBuiltOnceAndRegisteredAsMiddleware() {
        var application = Application();

        FunctionGeneratorHarness.AssertEmits(application,
            "var filterProvider = global::Hardened.Amz.Shared.Lambda.Runtime.ApplicationServices.Require<" +
            "global::Hardened.Amz.Function.Lambda.Runtime.Impl.ILambdaInvokeFilterProvider>(" +
            "RootServiceProvider, \"Application\", \"LambdaFunctionModule\");");
        FunctionGeneratorHarness.AssertEmits(application,
            "var handler = filterProvider.ProvideFilter(RootServiceProvider);");
        FunctionGeneratorHarness.AssertEmits(application, "middleware.Use(_ => handler);");
    }

    /// <summary>
    /// The application is an <c>IApplicationRoot</c>, so the host can reach its container and
    /// dispose it. Both members of that contract are emitted.
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
        FunctionGeneratorHarness.AssertEmits(Application(),
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
}
