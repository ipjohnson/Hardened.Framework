using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Shared;

/// <summary>
/// The application root every Hardened runtime emits, compiled rather than string-matched.
///
/// <para>
/// <c>ApplicationEntryPointFileWriter</c> and <c>ApplicationRootImplementation</c> decide what an
/// application's constructors, its <c>Provider</c> property and its <c>DisposeAsync</c> look like.
/// Everything they emit lands in a consumer's build, so the assertion that matters is that it
/// compiles; the string assertions alongside are about <em>which</em> of the several shapes was
/// chosen.
/// </para>
/// </summary>
public class ApplicationRootEmitTests {

    private const string Startup =
        "    private static Task<bool> Startup(IServiceProvider provider) => Task.FromResult(true);";

    /// <summary>
    /// Asserts the emitted file contains <paramref name="expected"/>, ignoring the layout the
    /// emitter chose — a call with more than two arguments is written one argument per line, and a
    /// test that pinned that would fail on a formatting change rather than on a behaviour one.
    /// </summary>
    private static void AssertEmits(string source, string expected) {
        static string Compact(string value) =>
            new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

        Assert.True(Compact(source).Contains(Compact(expected), StringComparison.Ordinal),
            $"the generated application root does not contain:{Environment.NewLine}  {expected}" +
            $"{Environment.NewLine}{Environment.NewLine}{source}");
    }

    [Fact]
    public void TheApplicationRootImplementsIApplicationRoot() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application());

        Assert.Contains("Hardened.Shared.Runtime.Application.IApplicationRoot", root);
        Assert.Contains("RootServiceProvider ?? throw new global::System.Exception", root);
        Assert.Contains("DisposeAsync", root);
    }

    /// <summary>
    /// The generated class is emitted as a partial of the entry point, so the runtime's own half —
    /// here the hand-written <c>CreateServiceProvider</c> — and the emitted half are one type. A
    /// writer that emitted a differently named or non-partial class produces two types and a
    /// CS0260 in every consumer.
    /// </summary>
    [Fact]
    public void TheApplicationRootIsAPartialOfTheEntryPointClass() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application());

        Assert.Contains("public partial class Application", root);
    }

    /// <summary>
    /// Recorded 2026-08-11. <c>DisposeAsync</c> assigned <c>RootServiceProvider = null</c> against a
    /// field declared non-nullable, so every generated application root carried a CS8625 and failed
    /// the build of any consumer using <c>TreatWarningsAsErrors</c> — which is how
    /// <c>Hardened.Amz</c>'s LambdaWebTest was building. It now emits <c>null!</c>, matching the
    /// <c>?? throw</c> in the <c>Provider</c> getter beside it.
    /// </summary>
    [Fact]
    public void DisposingTheRootClearsItsProviderWithoutANullableWarning() {
        var result = ApplicationRootHarness.GenerateWithDiagnostics(ApplicationRootHarness.Application());

        Assert.Contains("RootServiceProvider = null!;", result.SourceContaining("ApplicationRoot"));

        var nullableWarnings = result.CompilationDiagnostics
            .Where(diagnostic => diagnostic.Id == "CS8625")
            .ToArray();

        Assert.True(nullableWarnings.Length == 0,
            "the generated application root assigns null to a non-nullable field: " +
            string.Join(Environment.NewLine, nullableWarnings.Select(diagnostic => diagnostic.GetMessage())));
    }

    /// <summary>
    /// Disposal reads the field into a local before clearing it, so a second <c>DisposeAsync</c>
    /// finds the field already null and skips the dispose rather than disposing twice.
    /// </summary>
    [Fact]
    public void DisposalCapturesTheProviderBeforeClearingTheField() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application());

        var capture = root.IndexOf("currentRootServiceProvider = RootServiceProvider", StringComparison.Ordinal);
        var clear = root.IndexOf("RootServiceProvider = null!", StringComparison.Ordinal);

        Assert.True(capture >= 0, "the provider is never captured before being cleared");
        Assert.True(clear > capture, "the field is cleared before it is captured, so nothing is disposed");
        Assert.Contains("if (RootServiceProvider != null)", root);
    }

    /// <summary>
    /// A <c>Startup</c> method on the entry point is handed to <c>StartWithWait</c> as a method
    /// group. Miss it and the application starts without ever running the user's startup task.
    /// </summary>
    [Fact]
    public void AStartupMethodOnTheEntryPointIsWaitedOnAtConstruction() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application(Startup));

        AssertEmits(root, "StartWithWait(RootServiceProvider, Startup, 15)");
    }

    [Fact]
    public void AnEntryPointWithoutAStartupMethodStartsWithNoStartupTask() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application());

        AssertEmits(root, "StartWithWait(RootServiceProvider, null, 15)");
    }

    /// <summary>
    /// The parameterless constructor is what a consumer writes <c>new Application()</c> against. It
    /// chains to the environment overload with a default environment and no dependency overrides.
    /// </summary>
    [Fact]
    public void TheParameterlessConstructorSuppliesADefaultEnvironment() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application());

        AssertEmits(root, ": this(new global::Hardened.Shared.Runtime.Application.EnvironmentImpl(), null)");
    }

    /// <summary>
    /// <c>ConfigureLogging(ILoggingBuilder)</c> — the one-parameter form — is passed straight
    /// through as a method group.
    /// </summary>
    [Fact]
    public void AConfigureLoggingMethodTakingOnlyTheBuilderIsPassedAsAMethodGroup() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application("""
                private static void ConfigureLogging(ILoggingBuilder builder) { }
            """));

        AssertEmits(root,
            "CreateServiceProvider(environment, overrideDependencies, ConfigureLogging, RegisterInitDi)");
    }

    /// <summary>
    /// <c>ConfigureLogging(IHardenedEnvironment, ILoggingBuilder)</c> cannot be a method group for an
    /// <c>Action&lt;ILoggingBuilder&gt;</c>, so the environment is closed over in a lambda first.
    /// </summary>
    [Fact]
    public void AConfigureLoggingMethodTakingTheEnvironmentIsClosedOverInALambda() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application("""
                private static void ConfigureLogging(IHardenedEnvironment environment, ILoggingBuilder builder) { }
            """));

        AssertEmits(root, "loggingBuilderAction = builder => ConfigureLogging(environment, builder)");
        AssertEmits(root,
            "CreateServiceProvider(environment, overrideDependencies, loggingBuilderAction, RegisterInitDi)");
    }

    [Fact]
    public void AnEntryPointWithNoLoggingConfigurationPassesNoLoggingAction() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application());

        AssertEmits(root, "CreateServiceProvider(environment, overrideDependencies, null, RegisterInitDi)");
    }

    /// <summary>
    /// <c>RegisterInitDi</c> is emitted whether or not anything fills it in — it is the seam the
    /// module generators write registrations into, and the constructor already passes it.
    /// </summary>
    [Fact]
    public void TheRegisterInitDiSeamIsAlwaysEmitted() {
        var root = ApplicationRootHarness.Generate(ApplicationRootHarness.Application());

        Assert.Contains("private static void RegisterInitDi(", root);
    }

    /// <summary>
    /// The two hooks a runtime derives for: extra constructor statements, and extra methods on the
    /// application class. Both shipped writers in <c>Hardened.Amz</c> use both.
    /// </summary>
    [Fact]
    public void ADerivedWriterAddsItsOwnConstructorLogicAndDomainMethods() {
        var root = ApplicationRootHarness.Generate(
            ApplicationRootHarness.Application(), new DomainWriter());

        Assert.Contains("_handler = RootServiceProvider.GetRequiredService", root);
        Assert.Contains("Invoke()", root);
    }

    /// <summary>
    /// A derived writer that replaces <c>ImplementApplicationRoot</c> gets a class with no
    /// <c>IApplicationRoot</c> base and no provider field — the extension point exists so a runtime
    /// whose root is not disposable-shaped can opt out.
    /// </summary>
    [Fact]
    public void ADerivedWriterCanReplaceTheApplicationRootImplementation() {
        var root = ApplicationRootHarness.Generate(
            NoRootHarnessSource, new NoRootWriter());

        Assert.DoesNotContain("IApplicationRoot", root);
        Assert.DoesNotContain("DisposeAsync", root);
    }

    /// <summary>
    /// Without <c>ImplementApplicationRoot</c> there is no <c>RootServiceProvider</c> field, so the
    /// constructor's assignment target has to be declared by hand.
    /// </summary>
    private const string NoRootHarnessSource = """
        using System;
        using System.Threading.Tasks;
        using Hardened.Shared.Runtime.Application;
        using Hardened.Shared.Runtime.Attributes;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;

        namespace TestApp;

        [HardenedModule]
        public partial class Application {
            private ServiceProvider RootServiceProvider = null!;

            public ServiceProvider CreateServiceProvider(
                IHardenedEnvironment environment,
                Action<IHardenedEnvironment, IServiceCollection>? overrideDependencies,
                Action<ILoggingBuilder>? loggingBuilderAction,
                Action<IHardenedEnvironment, IServiceCollection>? initDependencies = null) =>
                new ServiceCollection().BuildServiceProvider();
        }
        """;

    /// <summary>
    /// The logger-factory extension point, in all three shapes it emits.
    ///
    /// <para>
    /// <c>SetupLoggerFactory</c> is <c>protected virtual</c> on a shipped abstract class and is the
    /// only reader of the abstract <c>LoggerHelper</c> every derived writer has to supply — but no
    /// writer in either repository calls it. It is covered here through a writer that does, because
    /// what it emits has to compile against a real logger helper, and the three branches pick
    /// different helper overloads.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("", "LoggerHelper.CreateAction(environment, \"TestApp\")")]
    [InlineData("    private static void ConfigureLogging(IHardenedEnvironment environment, ILoggingBuilder builder) { }",
        "LoggerFactory.Create(builder => ConfigureLogging(environment, builder))")]
    [InlineData("    private static LogLevel ConfigureLogLevel(IHardenedEnvironment environment) => LogLevel.Warning;",
        "LoggerHelper.CreateAction(ConfigureLogLevel(environment), \"TestApp\")")]
    public void TheLoggerFactoryIsBuiltFromWhicheverLoggingMethodTheEntryPointDeclares(
        string member, string expected) {
        var root = ApplicationRootHarness.Generate(
            ApplicationRootHarness.Application(member),
            new LoggerFactoryWriter(),
            ApplicationRootHarness.LoggerHelper);

        AssertEmits(root, expected);
    }

    /// <summary>A writer of the shape both Lambda runtimes use: a field, a service resolve, a method.</summary>
    private class DomainWriter : ApplicationEntryPointFileWriter {

        protected override ITypeDefinition LoggerHelper { get; } =
            TypeDefinition.Get("TestApp.Logging", "LoggerHelper");

        protected override void CustomConstructorLogic(
            EntryPointSelector.Model entryPoint, ClassDefinition appClass,
            ConstructorDefinition constructor, ParameterDefinition environment) {
            var provider = appClass.Fields.First(field => field.Name == "RootServiceProvider").Instance;

            var handler = appClass.AddField(KnownTypes.Requests.IMiddlewareService, "_handler");

            var resolve = provider.InvokeGeneric("GetRequiredService",
                new[] { KnownTypes.Requests.IMiddlewareService });

            // GetRequiredService is an extension method, and an extension method is reachable only
            // through a using of its namespace - global:: cannot name one. Every derived writer that
            // resolves a service has to say this, the four in Hardened.Amz included.
            resolve.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

            constructor.Assign(resolve).To(handler.Instance);
        }

        protected override void CreateDomainMethods(
            EntryPointSelector.Model model, ClassDefinition classDefinition) {
            var invoke = classDefinition.AddMethod("Invoke");

            invoke.Modifiers = ComponentModifier.Public;
            invoke.SetReturnType(KnownTypes.Requests.IMiddlewareService);
            invoke.Return(classDefinition.Fields.First(field => field.Name == "_handler").Instance);
        }
    }

    private class NoRootWriter : ApplicationEntryPointFileWriter {

        protected override ITypeDefinition LoggerHelper { get; } =
            TypeDefinition.Get("TestApp.Logging", "LoggerHelper");

        protected override void ImplementApplicationRoot(
            EntryPointSelector.Model model, ClassDefinition classDefinition) { }
    }

    private class LoggerFactoryWriter : ApplicationEntryPointFileWriter {

        protected override ITypeDefinition LoggerHelper { get; } =
            TypeDefinition.Get("TestApp.Logging", "LoggerHelper");

        protected override void CustomConstructorLogic(
            EntryPointSelector.Model entryPoint, ClassDefinition appClass,
            ConstructorDefinition constructor, ParameterDefinition environment) {
            SetupLoggerFactory(entryPoint, constructor, environment);
        }
    }
}
