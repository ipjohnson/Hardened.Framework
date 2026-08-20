using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.DependencyInjection;

/// <summary>
/// Runs <see cref="ServiceProviderFileGenerator"/> the way a shipped generator does, so the emitted
/// <c>CreateServiceProvider</c> is compiled rather than string-matched.
/// </summary>
public class ServiceProviderGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var provider = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(true));

        context.RegisterSourceOutput(
            provider, (production, model) => new ServiceProviderFileGenerator().GenerateFile(production, model));
    }
}

/// <summary>
/// The service collection every host without a <c>Program.cs</c> of its own is built from — Lambda
/// among them, through the writers in <c>Hardened.Amz</c>.
/// </summary>
public class ServiceProviderEmitTests {

    /// <summary>
    /// No <c>CreateServiceProvider</c> member of its own: that is the method under test, and
    /// declaring one here would collide with the emitted partial.
    ///
    /// <para>
    /// <c>PopulateServiceCollection</c> is the reverse — normally written by the DependencyModules
    /// generator, which is not running here, so the emitted call has nothing to bind against
    /// without it.
    /// </para>
    /// </summary>
    private const string Application = """
        using Hardened.Shared.Runtime.Attributes;
        using Microsoft.Extensions.DependencyInjection;

        namespace TestApp;

        [HardenedModule]
        public partial class Application {
            public void PopulateServiceCollection(IServiceCollection services) { }
        }
        """;

    private static string Generate() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Application },
            [new ServiceProviderGenerator()],
            RequestGeneratorHarness.Anchors);

        result.AssertNoErrors();

        return result.SourceContaining("ServiceProvider");
    }

    /// <summary>
    /// The environment has to be reachable as <c>IModuleEnvironment</c> as well as
    /// <c>IHardenedEnvironment</c>, because the module system reads the first while it decides what
    /// to register.
    /// </summary>
    /// <remarks>
    /// This was <c>AddSingleton(environment)</c>, which registers the parameter's static type
    /// alone. Every host built through this method — Lambda, and anything else without its own
    /// <c>Program.cs</c> — therefore had <c>[IfEnvironment]</c> answering against
    /// <c>ASPNETCORE_ENVIRONMENT</c>, defaulting to <c>Production</c>, while the same application
    /// read <c>HARDENED_ENVIRONMENT</c> and said <c>development</c>. It compiled and it ran; the
    /// only symptom was environment-gated services quietly not being registered.
    /// </remarks>
    [Fact]
    public void TheEnvironmentIsRegisteredUnderBothInterfaces() {
        var generated = Generate();

        Assert.Contains("AddHardenedEnvironment(environment)", generated);
        Assert.DoesNotContain("AddSingleton(environment)", generated);
    }

    /// <summary>
    /// The environment must be in the collection before the modules read it, so ordering here is
    /// behaviour rather than style.
    /// </summary>
    [Fact]
    public void TheEnvironmentIsRegisteredBeforeTheModulesAreApplied() {
        var generated = Generate();

        Assert.True(
            generated.IndexOf("AddHardenedEnvironment", StringComparison.Ordinal) <
            generated.IndexOf("PopulateServiceCollection", StringComparison.Ordinal),
            $"the environment is registered after the modules read it:{Environment.NewLine}{generated}");
    }
}
