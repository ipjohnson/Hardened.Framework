using System.Reflection;
using Hardened.DependencyModules.SourceGenerator;
using Hardened.Library.SourceGenerator;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Configuration;
using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Generator;

/// <summary>
/// <c>[FromEnvironmentVariable]</c> end to end: the generator writes the read, the generated
/// assembly is emitted and loaded, and the values are resolved through a real
/// <see cref="ConfigurationManager"/> against a real <see cref="EnvironmentImpl"/>.
///
/// <para>
/// Going through the loaded assembly rather than the emitted string is the point. The documented
/// behaviour — "reads the variable when the model is first constructed, falling back to the field's
/// initialiser when the variable is unset or empty" — is a collaboration between the generated init
/// action, <c>EnvironmentImpl.Value&lt;T&gt;</c> and <c>NewConfigurationValueProvider</c>. No
/// assertion on generated source can see whether they agree.
/// </para>
///
/// <para>
/// Every environment here is dictionary-backed. Process environment variables are global to the
/// test host, so a suite that sets them leaks into every other test running in the same process.
/// </para>
/// </summary>
public class FromEnvironmentVariableTests {

    private static readonly Type[] Anchors = [
        typeof(ConfigurationModelAttribute),  // Hardened.Shared.Runtime
        typeof(IConfigurationPackage),        // Hardened.Shared.Runtime
        typeof(IServiceCollection)            // Microsoft.Extensions.DependencyInjection.Abstractions
    ];

    private const string ModelSource = """
        using System;
        using Hardened.Shared.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestModule { }

        [ConfigurationModel]
        public partial class ServiceOptions {
            [FromEnvironmentVariable("SERVICE_URL")]
            private string _serviceUrl = "http://default";

            [FromEnvironmentVariable("RETENTION_DAYS")]
            private int _retentionDays = 180;

            [FromEnvironmentVariable("VERBOSE")]
            private bool _verbose = false;

            private string _notFromTheEnvironment = "untouched";
        }
        """;

    /// <summary>
    /// Compiles a generator run into a real assembly and loads it, so the generated wiring can be
    /// executed rather than read. Each run gets its own assembly name: the loader tolerates two
    /// assemblies with the same simple name, but nothing good comes of relying on that.
    /// </summary>
    private static Assembly GeneratedAssembly(string assemblyName) {
        var result = GeneratorTestHarness.Run(
                new Dictionary<string, string> { ["Test.cs"] = ModelSource },
                [new LibrarySourceGenerator(), new HardenedSourceGenerator()],
                Anchors,
                assemblyName: assemblyName)
            .AssertNoErrors();

        using var stream = new MemoryStream();

        var emitResult = result.Compilation.Emit(stream);

        Assert.True(emitResult.Success,
            "The generated assembly did not emit: " +
            string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        return Assembly.Load(stream.ToArray());
    }

    /// <summary>
    /// The resolved configuration, as the application would get it: the module's generated
    /// <c>ConfigurationProvider</c> handed to a <see cref="ConfigurationManager"/>.
    /// </summary>
    private static object Resolve(Assembly assembly, IHardenedEnvironment environment) {
        var package = (IConfigurationPackage)Activator.CreateInstance(
            assembly.GetType("TestApp.TestModule+ConfigurationProvider")!)!;

        return Resolve(new ConfigurationManager(environment, [package]), assembly);
    }

    private static object Resolve(IConfigurationManager manager, Assembly assembly) {
        try {
            return typeof(IConfigurationManager)
                .GetMethod(nameof(IConfigurationManager.GetConfiguration))!
                .MakeGenericMethod(assembly.GetType("TestApp.IServiceOptions")!)
                .Invoke(manager, null)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null) {
            // Reflection wraps whatever the configuration read threw. The wrapper is noise; the
            // exception the application would actually see is the inner one.
            throw exception.InnerException;
        }
    }

    private static object? Read(object configuration, string property) =>
        configuration.GetType().GetProperty(property)!.GetValue(configuration);

    private static EnvironmentImpl EnvironmentWith(params (string Name, string Value)[] values) =>
        new(name: "development",
            environmentValues: values.ToDictionary(pair => pair.Name, pair => pair.Value));

    /// <summary>
    /// Documented: the variable falls back to "the field's initialiser when the variable is unset".
    /// </summary>
    [Fact]
    public void AnUnsetVariableLeavesTheFieldInitialiserInPlace() {
        var configuration = Resolve(GeneratedAssembly("EnvUnset"), EnvironmentWith());

        Assert.Equal("http://default", Read(configuration, "ServiceUrl"));
        Assert.Equal(180, Read(configuration, "RetentionDays"));
        Assert.Equal(false, Read(configuration, "Verbose"));
    }

    /// <summary>
    /// Documented: "unset or empty". Empty is the case that matters in practice — a deployment
    /// template that always sets the variable and sometimes sets it to nothing would otherwise
    /// overwrite every default with "".
    /// </summary>
    [Fact]
    public void AnEmptyVariableLeavesTheFieldInitialiserInPlace() {
        var configuration = Resolve(
            GeneratedAssembly("EnvEmpty"),
            EnvironmentWith(("SERVICE_URL", ""), ("RETENTION_DAYS", "")));

        Assert.Equal("http://default", Read(configuration, "ServiceUrl"));
        Assert.Equal(180, Read(configuration, "RetentionDays"));
    }

    [Fact]
    public void ASetVariableReplacesTheFieldInitialiser() {
        var configuration = Resolve(
            GeneratedAssembly("EnvSet"),
            EnvironmentWith(("SERVICE_URL", "http://from-environment")));

        Assert.Equal("http://from-environment", Read(configuration, "ServiceUrl"));
    }

    /// <summary>
    /// Documented: "The value is converted to the field's type, so an <c>int</c> field backed by
    /// <c>RETENTION_DAYS=90</c> arrives as <c>90</c>."
    /// </summary>
    [Fact]
    public void AValueIsConvertedToTheFieldsType() {
        var configuration = Resolve(
            GeneratedAssembly("EnvConvert"),
            EnvironmentWith(("RETENTION_DAYS", "90"), ("VERBOSE", "true")));

        Assert.Equal(90, Read(configuration, "RetentionDays"));
        Assert.Equal(true, Read(configuration, "Verbose"));
    }

    /// <summary>
    /// A value that cannot be converted fails loudly at first resolution rather than silently
    /// falling back to the default. Starting with a misconfigured retention window and no error is
    /// the worse outcome.
    /// </summary>
    [Fact]
    public void AValueThatCannotBeConvertedThrows() {
        var assembly = GeneratedAssembly("EnvBadConvert");

        Assert.Throws<FormatException>(
            () => Resolve(assembly, EnvironmentWith(("RETENTION_DAYS", "ninety"))));
    }

    /// <summary>A field with no attribute is not touched by the environment at all.</summary>
    [Fact]
    public void AFieldWithNoAttributeIsNeverReadFromTheEnvironment() {
        var configuration = Resolve(
            GeneratedAssembly("EnvUnattributed"),
            EnvironmentWith(("NotFromTheEnvironment", "changed"), ("_notFromTheEnvironment", "changed")));

        Assert.Equal("untouched", Read(configuration, "NotFromTheEnvironment"));
    }

    /// <summary>
    /// Documented: "Reading happens once. Configuration models are cached per type for the life of
    /// the application, so a variable changed after startup is not picked up — which is what you
    /// want on Lambda, where the process outlives many invocations."
    /// </summary>
    [Fact]
    public void AVariableChangedAfterTheFirstResolutionIsNotPickedUp() {
        var assembly = GeneratedAssembly("EnvCached");
        var values = new Dictionary<string, string> { ["SERVICE_URL"] = "http://first" };

        var package = (IConfigurationPackage)Activator.CreateInstance(
            assembly.GetType("TestApp.TestModule+ConfigurationProvider")!)!;

        var manager = new ConfigurationManager(
            new EnvironmentImpl(name: "development", environmentValues: values), [package]);

        var first = Resolve(manager, assembly);

        values["SERVICE_URL"] = "http://second";

        var second = Resolve(manager, assembly);

        Assert.Same(first, second);
        Assert.Equal("http://first", Read(second, "ServiceUrl"));
    }

    /// <summary>
    /// The generated provider registers under the generated interface, so asking for a type nothing
    /// registered still throws — the model being present in the assembly is not enough.
    /// </summary>
    [Fact]
    public void AModelTheModuleDidNotContributeIsStillUnregistered() {
        var assembly = GeneratedAssembly("EnvUnregistered");

        var package = (IConfigurationPackage)Activator.CreateInstance(
            assembly.GetType("TestApp.TestModule+ConfigurationProvider")!)!;

        var manager = new ConfigurationManager(EnvironmentWith(), [package]);

        var exception = Assert.Throws<Exception>(() => manager.GetConfiguration<UnrelatedConfiguration>());

        Assert.Contains(nameof(UnrelatedConfiguration), exception.Message);
    }

    private class UnrelatedConfiguration;
}
