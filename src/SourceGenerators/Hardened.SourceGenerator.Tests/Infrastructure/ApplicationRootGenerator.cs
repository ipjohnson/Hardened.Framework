using CSharpAuthor;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Writes an application root for every <c>[HardenedModule]</c> class, through the shared base
/// writer the shipped runtimes derive from.
///
/// <para>
/// <see cref="ApplicationEntryPointFileWriter"/> is abstract and has no derived class inside
/// <c>Hardened.SourceGenerator</c> — the four that exist live in <c>Hardened.Amz</c>. It is still
/// this repository's code, and it is the class that decides what every Hardened application's
/// constructor, <c>Provider</c> property and <c>DisposeAsync</c> look like, so it is tested here
/// through a derived writer of the same shape as the shipped ones.
/// </para>
/// </summary>
public class ApplicationRootGenerator(ApplicationEntryPointFileWriter writer) : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var provider = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(true));

        context.RegisterSourceOutput(provider, (production, model) => {
            var file = new CSharpFileDefinition(model.EntryPointType.Namespace);

            writer.CreateApplicationClass(model, file);

            var output = new OutputContext(
                new OutputContextOptions { TypeOutputMode = TypeOutputMode.Global });

            file.WriteOutput(output);

            production.AddSource(model.EntryPointType.Name + ".ApplicationRoot.cs", output.Output());
        });
    }
}

/// <summary>
/// The plainest writer a runtime can supply: everything default except the logger helper, which is
/// abstract.
/// </summary>
public class TestApplicationFileWriter : ApplicationEntryPointFileWriter {

    protected override ITypeDefinition LoggerHelper { get; } =
        TypeDefinition.Get("TestApp.Logging", "LoggerHelper");
}

public static class ApplicationRootHarness {

    /// <summary>
    /// The half of the application a Hardened runtime supplies by hand or through its own
    /// generators, so the emitted half has something to bind against.
    ///
    /// <para>
    /// <c>CreateServiceProvider</c> is normally written by
    /// <c>DependencyInjection/ServiceProviderFileGenerator</c>. It is declared here instead so the
    /// case under test is the entry-point writer alone; its signature is that generator's, parameter
    /// for parameter, because the emitted constructor calls it positionally.
    /// </para>
    /// </summary>
    public static string Application(string members = "", string attributes = "") => $$"""
        using System;
        using System.Threading.Tasks;
        using Hardened.Shared.Runtime.Application;
        using Hardened.Shared.Runtime.Attributes;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;

        namespace TestApp;

        [HardenedModule]
        {{attributes}}
        public partial class Application {
        {{members}}

            public ServiceProvider CreateServiceProvider(
                IHardenedEnvironment environment,
                Action<IHardenedEnvironment, IServiceCollection>? overrideDependencies,
                Action<ILoggingBuilder>? loggingBuilderAction,
                Action<IHardenedEnvironment, IServiceCollection>? initDependencies = null) {
                var services = new ServiceCollection();

                overrideDependencies?.Invoke(environment, services);
                initDependencies?.Invoke(environment, services);

                return services.BuildServiceProvider();
            }
        }
        """;

    /// <summary>
    /// A stand-in for the logger helper each runtime ships — <c>LambdaLoggerHelper</c> and
    /// <c>LambdaWebLoggerHelper</c> both have exactly these two overloads, and the emitted call
    /// picks between them by whether the entry point declares <c>ConfigureLogLevel</c>.
    /// </summary>
    /// <remarks>
    /// A second file rather than a second namespace in the same one: a file-scoped namespace and a
    /// block namespace cannot share a file (CS8955), and that error lands in the test's own source
    /// where it reads as a generator failure.
    /// </remarks>
    public const string LoggerHelper = """
        using Microsoft.Extensions.Logging;
        using Hardened.Shared.Runtime.Application;

        namespace TestApp.Logging;

        public static class LoggerHelper {
            public static System.Action<ILoggingBuilder> CreateAction(
                IHardenedEnvironment environment, string entryNamespace) => _ => { };

            public static System.Action<ILoggingBuilder> CreateAction(
                LogLevel logLevel, string entryNamespace) => _ => { };
        }
        """;

    /// <summary>
    /// Generates the application root for <paramref name="source"/> and compiles it, then returns
    /// the emitted file.
    /// </summary>
    /// <remarks>
    /// The generated-file assertion is not redundant with <c>AssertNoErrors</c>: a writer that threw
    /// is reported by <c>SourceGeneratorWrapper</c> as a Warning, leaving no output and a passing
    /// compilation. See <c>GeneratorCrashHandlingTests</c>.
    /// </remarks>
    public static string Generate(
        string source, ApplicationEntryPointFileWriter? writer = null, string? loggerHelper = null) {
        var result = GenerateWithDiagnostics(source, writer, loggerHelper);

        result.AssertNoErrors();

        Assert.Empty(result.GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == "HardenedException"));

        return result.SourceContaining("ApplicationRoot");
    }

    /// <summary>
    /// Every diagnostic the compilation reported for the generated application root, so a test can
    /// assert on warnings as well as errors — the CS8625 below shipped for months as a warning.
    /// </summary>
    public static GeneratorResult GenerateWithDiagnostics(
        string source, ApplicationEntryPointFileWriter? writer = null, string? loggerHelper = null) {
        var sources = new Dictionary<string, string> { ["Test.cs"] = source };

        if (loggerHelper != null) {
            sources["LoggerHelper.cs"] = loggerHelper;
        }

        return GeneratorTestHarness.Run(
            sources,
            [new ApplicationRootGenerator(writer ?? new TestApplicationFileWriter())],
            RequestGeneratorHarness.Anchors);
    }
}
