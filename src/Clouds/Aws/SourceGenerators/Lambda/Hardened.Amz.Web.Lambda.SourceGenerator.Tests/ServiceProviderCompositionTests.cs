using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// The framework's <c>CreateServiceProvider</c> writer, which every other test in this project
/// stands in for by hand.
/// </summary>
public class ServiceProviderGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var entryPoints = context.SyntaxProvider
            .CreateSyntaxProvider(EntryPointSelector.UsingAttribute(), EntryPointSelector.TransformModel(true))
            .WithComparer(new EntryPointSelector.Comparer());

        var writer = new ServiceProviderFileGenerator();

        context.RegisterSourceOutput(entryPoints, writer.GenerateFile);
    }
}

/// <summary>
/// The emitted constructor calling the emitted <c>CreateServiceProvider</c>, with nothing written by
/// hand in between.
///
/// <para>
/// Everywhere else in this project the application source declares <c>CreateServiceProvider</c>
/// itself, with a comment claiming its signature is <c>ServiceProviderFileGenerator</c>'s "parameter
/// for parameter". That claim is the weak point of every one of those tests: if the real generator's
/// signature drifted, they would all keep passing against a stand-in nobody ships. Here the real one
/// is run, so the call and the method are both generated and the compiler checks they agree.
/// </para>
/// </summary>
public class ServiceProviderCompositionTests {

    /// <summary>
    /// An application whose only hand-written member is <c>PopulateServiceCollection</c>.
    ///
    /// <para>
    /// That method is DependencyModules' half — <c>Application.Module.g.cs</c> in a real build,
    /// where its body loads the module registry — and the emitted <c>CreateServiceProvider</c> calls
    /// it on <c>this</c>. Running the DependencyModules generator here as well would test that
    /// package rather than this one, so it is the one stand-in left: its signature is that
    /// generator's, and its body is empty because nothing here asserts on what it registers.
    /// </para>
    /// </summary>
    private static string Application(string members = "") => $$"""
        using System;
        using System.Threading.Tasks;
        using Hardened.Shared.Runtime.Application;
        using Hardened.Shared.Runtime.Attributes;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;

        namespace TestApp;

        [HardenedModule]
        public partial class Application {
        {{members}}

            public void PopulateServiceCollection(IServiceCollection services) { }
        }
        """;

    private static Hardened.Amz.SourceGeneration.Testing.GeneratorResult Generate(string source) {
        var result = WebGeneratorHarness.Run(
            [new WebLambdaSourceGenerator(), new ServiceProviderGenerator()], source);

        result.AssertNoErrors();
        WebGeneratorHarness.AssertDidNotCrash(result);

        return result;
    }

    /// <summary>
    /// The constructor this generator emits calls the method the framework emits, positionally, and
    /// the two compile together.
    /// </summary>
    [Fact]
    public void TheEmittedConstructorCallsTheEmittedServiceProviderFactory() {
        var result = Generate(Application());

        Assert.Contains("Application.App.cs", result.GeneratedSources.Keys);
        Assert.Contains("Application.ServiceProvider.cs", result.GeneratedSources.Keys);

        WebGeneratorHarness.AssertEmits(result.SourceContaining("App.cs"),
            "CreateServiceProvider(environment, overrideDependencies, null, RegisterInitDi)");
        Assert.Contains(
            "public global::Microsoft.Extensions.DependencyInjection.ServiceProvider CreateServiceProvider(",
            result.SourceContaining("ServiceProvider.cs"));
    }

    /// <summary>
    /// <c>RegisterInitDi</c> is passed as the factory's <c>initDependencies</c> argument, and the
    /// factory invokes it before the consumer's overrides. The seam only works if the method this
    /// generator emits matches the delegate the framework's factory declares.
    /// </summary>
    [Fact]
    public void TheRegisterInitDiSeamBindsToTheFactorysInitialisationDelegate() {
        Assert.Contains("initDependencies?.Invoke(environment, serviceCollection);",
            Generate(Application()).SourceContaining("ServiceProvider.cs"));
    }

    /// <summary>
    /// The <c>ConfigureLogging</c> overload passed as a method group has to match the factory's
    /// <c>Action&lt;ILoggingBuilder&gt;</c> parameter exactly. This is the case that depends on the
    /// two generators agreeing about a delegate type rather than a call shape.
    /// </summary>
    [Fact]
    public void AMethodGroupLoggingConfigurationBindsToTheEmittedFactorysDelegateParameter() {
        Generate(Application("    private static void ConfigureLogging(ILoggingBuilder builder) { }"));
    }

    /// <summary>
    /// The lambda form, which closes over the constructor's <c>environment</c> parameter and is
    /// assigned to a local of the delegate type before being passed.
    /// </summary>
    [Fact]
    public void ALambdaLoggingConfigurationBindsToTheEmittedFactorysDelegateParameter() {
        Generate(Application(
            "    private static void ConfigureLogging(IHardenedEnvironment environment, ILoggingBuilder builder) { }"));
    }

    /// <summary>
    /// A <c>Startup</c> method is passed to <c>StartWithWait</c> as a method group, so its signature
    /// has to match what the runtime expects of one.
    /// </summary>
    [Fact]
    public void AStartupMethodBindsToWhatStartWithWaitExpects() {
        Generate(Application(
            "    private static Task<bool> Startup(IServiceProvider provider) => Task.FromResult(true);"));
    }
}
