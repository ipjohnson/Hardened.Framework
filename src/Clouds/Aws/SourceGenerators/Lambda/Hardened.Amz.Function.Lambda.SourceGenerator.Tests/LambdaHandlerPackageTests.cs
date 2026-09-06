using Xunit;

namespace Hardened.Amz.Function.Lambda.SourceGenerator.Tests;

/// <summary>
/// <c>Application.LambdaHandlerPackage.cs</c> — how an invocation finds the handler that should run.
///
/// <para>
/// AWS gives a Lambda invocation nothing but its <c>ILambdaContext</c>, and the only routing
/// information on it is <c>FunctionName</c>. <see cref="LambdaHandlerPackageFileWriter"/> emits the
/// nested class that turns that name into a handler by asking each registered
/// <c>IFunctionHandlerProvider</c> in turn, and the DI registration that makes the class reachable.
/// </para>
/// </summary>
public class LambdaHandlerPackageTests {

    private static string Package(string members = "") =>
        FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application(members))
            .SourceContaining("LambdaHandlerPackage");

    /// <summary>
    /// The lookup contract: the context's function name is what is offered to each provider, and the
    /// first provider returning a handler wins. Passing anything else — the whole context, or a
    /// constant — would make every invocation resolve the same handler.
    /// </summary>
    [Fact]
    public void AHandlerIsResolvedByTheFunctionNameOnTheInvocationContext() {
        var package = Package();

        FunctionGeneratorHarness.AssertEmits(package,
            "var handler = provider.GetFunctionHandler(context.FunctionName, serviceProvider);");
    }

    /// <summary>
    /// Providers are tried in order and the search stops at the first hit; a package that ran every
    /// provider and returned the last would give the registration order the opposite meaning.
    /// </summary>
    [Fact]
    public void TheFirstProviderToReturnAHandlerEndsTheSearch() {
        var package = Package();

        FunctionGeneratorHarness.AssertEmits(package, "foreach(var provider in _providers)");
        FunctionGeneratorHarness.AssertEmits(package, "if (handler != null) { return handler; }");
    }

    /// <summary>
    /// No provider recognising the function name is a null return, not an exception. The runtime
    /// turns that into a Lambda-level failure with its own message; a throw from here would surface
    /// as a generator-shaped stack trace instead.
    /// </summary>
    [Fact]
    public void AFunctionNameNoProviderRecognisesResolvesToNull() {
        var package = Package();

        var loop = package.IndexOf("foreach", StringComparison.Ordinal);
        var fallthrough = package.LastIndexOf("return", StringComparison.Ordinal);

        Assert.True(fallthrough > loop, "there is no return after the provider loop");
        FunctionGeneratorHarness.AssertEmits(package[loop..], "return null;");
    }

    /// <summary>
    /// The providers are injected as a collection, so every module contributing handlers is
    /// consulted. Taking a single <c>IFunctionHandlerProvider</c> would resolve only the last
    /// registration and silently drop the rest.
    /// </summary>
    [Fact]
    public void EveryRegisteredProviderIsInjectedRatherThanOnlyTheLast() {
        Assert.Contains(
            "public LambdaHandlerPackage(global::System.Collections.Generic.IEnumerable<" +
            "global::Hardened.Requests.Abstract.Execution.IFunctionHandlerProvider> providers)",
            Package());
    }

    /// <summary>
    /// The package implements the runtime's interface and is registered as a singleton against it —
    /// the class is private, so this registration is the only way the runtime can reach it.
    /// </summary>
    [Fact]
    public void ThePackageIsRegisteredAsTheSingletonImplementationOfTheRuntimeInterface() {
        var package = Package();

        Assert.Contains(
            "private class LambdaHandlerPackage : " +
            "global::Hardened.Amz.Function.Lambda.Runtime.Impl.ILambdaHandlerPackage",
            package);

        // The implementation is named in full. It is a nested private class, and from CSharpAuthor
        // 2.0 a bare name would be written global::LambdaHandlerPackage, which resolves to nothing.
        FunctionGeneratorHarness.AssertEmits(package,
            "serviceCollection.AddSingleton<" +
            "global::Hardened.Amz.Function.Lambda.Runtime.Impl.ILambdaHandlerPackage, " +
            "global::TestApp.Application.LambdaHandlerPackage>();");
    }

    /// <summary>
    /// The registration reaches the container through <c>DependencyRegistry</c>, which is how
    /// DependencyModules collects registrations contributed by generated partials. The static field
    /// is what runs it, and its <c>[DynamicDependency]</c> is what stops the trimmer removing the
    /// method the field points at.
    /// </summary>
    [Fact]
    public void TheRegistrationIsContributedThroughDependencyRegistryAndSurvivesTrimming() {
        var package = Package();

        FunctionGeneratorHarness.AssertEmits(package,
            "private static int _lambdaPackageDi = DependencyRegistry<Application>.Add(LambdaPackageDi)");
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.DynamicDependency(nameof(LambdaPackageDi))]",
            package);
    }

    /// <summary>
    /// <c>DependencyRegistry&lt;T&gt;</c> is keyed on the entry point type, so an application not
    /// named <c>Application</c> registers against itself rather than against a hard-coded name.
    /// </summary>
    [Fact]
    public void TheRegistryIsKeyedOnTheEntryPointRatherThanAFixedName() {
        var result = FunctionGeneratorHarness.Generate(
            FunctionGeneratorHarness.NamedApplication("OrderProcessor"));

        FunctionGeneratorHarness.AssertEmits(result.SourceContaining("LambdaHandlerPackage"),
            "DependencyRegistry<OrderProcessor>.Add(LambdaPackageDi)");
    }

    /// <summary>
    /// The handler package is emitted once per entry point. Until 2026-09-04 two generators
    /// registered the same writer behind exclusive selectors; there is one generator now, and this
    /// pins that the file is still emitted exactly once.
    /// </summary>
    [Fact]
    public void TheHandlerPackageIsEmittedOnce() {
        var result = FunctionGeneratorHarness.Run(new LambdaFunctionSourceGenerator(), FunctionGeneratorHarness.Application());

        result.AssertNoErrors();
        FunctionGeneratorHarness.AssertDidNotCrash(result);

        Assert.Empty(result.DuplicateHintNames);
        Assert.Contains("Application.LambdaHandlerPackage.cs", result.GeneratedSources.Keys);
    }
}
