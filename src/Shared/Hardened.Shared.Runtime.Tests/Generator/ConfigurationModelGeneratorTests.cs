using Hardened.DependencyModules.SourceGenerator;
using Hardened.Library.SourceGenerator;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Configuration;
using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Generator;

/// <summary>
/// The configuration generator, driven through <see cref="LibrarySourceGenerator"/> — the analyzer a
/// consumer actually gets — and asserted with <see cref="GeneratorResult.AssertNoErrors"/> so that
/// every case here proves the emitted C# builds, not merely that it contains the expected characters.
///
/// <para>
/// The behaviours asserted are the ones documented in <c>Hardened.Docs/website/guide/configuration.md</c>:
/// an interface named <c>I</c> + the class name, a property per field with the leading underscore
/// removed and the first letter capitalised, the field initialiser as the default, and
/// <c>[HideConfigurationField]</c> keeping a field out of the generated surface.
/// </para>
/// </summary>
public class ConfigurationModelGeneratorTests {

    /// <summary>
    /// The assemblies a configuration model binds against. <c>typeof</c> rather than a name so the
    /// assembly is loaded by the time references are collected.
    /// </summary>
    private static readonly Type[] Anchors = [
        typeof(ConfigurationModelAttribute),  // Hardened.Shared.Runtime
        typeof(IConfigurationPackage),        // Hardened.Shared.Runtime
        typeof(IServiceCollection)            // Microsoft.Extensions.DependencyInjection.Abstractions
    ];

    /// <summary>
    /// Both generators a consumer installs. Hardened.Library.SourceGenerator packs
    /// Hardened.DependencyModules.SourceGenerator alongside itself, and a [HardenedModule] class is
    /// only complete when both have run — the configuration entry point calls
    /// PopulateServiceCollection, which the other generator writes.
    /// </summary>
    private static GeneratorResult Generate(string source) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            [new LibrarySourceGenerator(), new HardenedSourceGenerator()],
            Anchors);

    private static GeneratorResult GenerateModel(string body, string className = "ServiceOptions") =>
        Generate($$"""
            using System;
            using System.Collections.Generic;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [ConfigurationModel]
            public partial class {{className}} {
            {{body}}
            }
            """);

    private static string PropertiesOf(GeneratorResult result, string className = "ServiceOptions") =>
        result.SourceContaining("ConfigurationModels_" + className);

    /// <summary>
    /// The emitter breaks an argument list across lines once it is long enough, so an assertion on a
    /// call has to ignore layout or it asserts the formatter rather than the call.
    /// </summary>
    private static string WithoutWhitespace(string source) =>
        new string(source.Where(character => !char.IsWhiteSpace(character)).ToArray());

    [Fact]
    public void AModelOfPlainFieldsCompiles() {
        GenerateModel("""
                private string _serviceUrl = "";
                private int _retentionDays = 180;
            """).AssertNoErrors();
    }

    /// <summary>
    /// Documented: "An interface, named <c>I</c> + the class name." Everything else in the framework
    /// depends on that interface, so the name is part of the contract rather than an implementation
    /// detail.
    /// </summary>
    [Fact]
    public void TheGeneratedInterfaceIsINameOfTheModel() {
        var result = GenerateModel("""
                private string _serviceUrl = "";
            """).AssertNoErrors();

        Assert.Contains("interface IServiceOptions", PropertiesOf(result));
        Assert.NotNull(result.Compilation.GetTypeByMetadataName("TestApp.IServiceOptions"));
    }

    /// <summary>The model is made to implement the interface the generator invented for it.</summary>
    [Fact]
    public void TheModelImplementsItsGeneratedInterface() {
        var result = GenerateModel("""
                private string _serviceUrl = "";
            """).AssertNoErrors();

        var model = result.Compilation.GetTypeByMetadataName("TestApp.ServiceOptions");
        var iface = result.Compilation.GetTypeByMetadataName("TestApp.IServiceOptions");

        Assert.NotNull(model);
        Assert.NotNull(iface);
        Assert.Contains(iface, model.AllInterfaces, SymbolEqualityComparer.Default);
    }

    /// <summary>
    /// Documented: "named from the field with its leading underscore removed and its first letter
    /// capitalised". The single-character case falls into a separate branch that upper-cases the
    /// whole name, a field with no underscore at all keeps its spelling, and leading underscores are
    /// stripped as a run rather than one at a time.
    /// </summary>
    [Theory]
    [InlineData("_serviceUrl", "ServiceUrl")]
    [InlineData("_retentionDays", "RetentionDays")]
    [InlineData("_URL", "URL")]
    [InlineData("serviceUrl", "ServiceUrl")]
    [InlineData("__doubled", "Doubled")]
    [InlineData("_x", "X")]
    [InlineData("x", "X")]
    public void APropertyIsNamedAfterItsFieldWithoutTheLeadingUnderscore(string field, string property) {
        var result = GenerateModel($"        private string {field} = \"\";").AssertNoErrors();

        Assert.NotNull(
            result.Compilation.GetTypeByMetadataName("TestApp.ServiceOptions")!
                .GetMembers(property)
                .FirstOrDefault());
    }

    /// <summary>
    /// Two fields on one line share a declaration, so a generator that reads the declaration rather
    /// than each declarator would silently emit one property instead of two.
    /// </summary>
    [Fact]
    public void EveryDeclaratorInASharedDeclarationGetsItsOwnProperty() {
        var result = GenerateModel("        private string _first = \"a\", _second = \"b\";").AssertNoErrors();

        var model = result.Compilation.GetTypeByMetadataName("TestApp.ServiceOptions")!;

        Assert.NotEmpty(model.GetMembers("First"));
        Assert.NotEmpty(model.GetMembers("Second"));
    }

    /// <summary>
    /// Documented: "The field's initialiser is the default." The generated property reads the field,
    /// so the initialiser survives without the generator copying it anywhere — which is exactly why
    /// this needs asserting rather than assuming.
    /// </summary>
    [Fact]
    public void ThePropertyReadsTheFieldSoTheInitialiserIsTheDefault() {
        var result = GenerateModel("""
                private int _retentionDays = 180;
            """).AssertNoErrors();

        var properties = PropertiesOf(result);

        Assert.Contains("_retentionDays;", properties);
        Assert.Contains("_retentionDays = value;", properties);
    }

    /// <summary>
    /// Documented: "To keep a field out of the generated interface entirely — a factory, a secret,
    /// something with no sensible property — mark it <c>[HideConfigurationField]</c>."
    /// </summary>
    [Fact]
    public void AHiddenFieldGetsNoProperty() {
        var result = GenerateModel("""
                private string _visible = "";

                [HideConfigurationField]
                private string _secret = "";
            """).AssertNoErrors();

        var model = result.Compilation.GetTypeByMetadataName("TestApp.ServiceOptions")!;

        Assert.NotEmpty(model.GetMembers("Visible"));
        Assert.Empty(model.GetMembers("Secret"));
    }

    /// <summary>The attribute is honoured under its full name as well as its short form.</summary>
    [Theory]
    [InlineData("HideConfigurationField")]
    [InlineData("HideConfigurationFieldAttribute")]
    public void AHiddenFieldIsRecognisedByEitherSpellingOfTheAttribute(string attribute) {
        var result = GenerateModel($$"""
                [{{attribute}}]
                private string _secret = "";
            """).AssertNoErrors();

        Assert.Empty(result.Compilation.GetTypeByMetadataName("TestApp.ServiceOptions")!.GetMembers("Secret"));
    }

    /// <summary>
    /// Hiding every field leaves an empty interface and an empty partial rather than nothing at all;
    /// the model still has to satisfy its own interface.
    /// </summary>
    [Fact]
    public void AModelWhoseFieldsAreAllHiddenStillCompiles() {
        GenerateModel("""
                [HideConfigurationField]
                private string _secret = "";
            """).AssertNoErrors();
    }

    /// <summary>A model with no fields at all is the degenerate case, and is not an error.</summary>
    [Fact]
    public void AModelWithNoFieldsCompiles() {
        GenerateModel("").AssertNoErrors();
    }

    /// <summary>
    /// Documented under "Fields that are not simple values": a field may hold a delegate or a
    /// dictionary of them, which is how a model says "the default is computed, and the application
    /// may replace it wholesale". These are the field types most likely to be emitted with the wrong
    /// arity or a lost generic argument.
    /// </summary>
    [Theory]
    [InlineData("Dictionary<string, string>", "Dictionary<string, string>")]
    [InlineData("Dictionary<string, Func<IServiceProvider, string>>", "Dictionary<string, Func<IServiceProvider, string>>")]
    [InlineData("Func<IServiceProvider, string>", "Func<IServiceProvider, string>")]
    [InlineData("Action<string>", "Action<string>")]
    [InlineData("IReadOnlyList<int>", "IReadOnlyList<int>")]
    [InlineData("List<KeyValuePair<string, int>>", "List<KeyValuePair<string, int>>")]
    [InlineData("string[]", "string[]")]
    [InlineData("int?", "int?")]
    [InlineData("TimeSpan", "TimeSpan")]
    [InlineData("System.Text.Json.JsonSerializerOptions", "JsonSerializerOptions")]
    public void ANonTrivialFieldTypeSurvivesIntoTheProperty(string fieldType, string expectedPropertyType) {
        var result = GenerateModel($"        private {fieldType} _value = default!;").AssertNoErrors();

        var property = result.Compilation
            .GetTypeByMetadataName("TestApp.ServiceOptions")!
            .GetMembers("Value")
            .OfType<IPropertySymbol>()
            .Single();

        Assert.Equal(
            expectedPropertyType,
            property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    /// <summary>
    /// A nullable delegate field, the shape <c>DynamoDbOptions._defaultClient</c> uses. The trailing
    /// <c>?</c> is handled on a separate path from the type itself.
    /// </summary>
    [Fact]
    public void ANullableDelegateFieldCompiles() {
        GenerateModel("        private Func<IServiceProvider, string>? _defaultClient;").AssertNoErrors();
    }

    /// <summary>
    /// The shape shipped in <c>Hardened.Amz.DynamoDbClient.DynamoDbOptions</c>: two environment-backed
    /// strings, a dictionary of factories and a nullable factory, all in one model.
    /// </summary>
    [Fact]
    public void TheShippedDynamoDbOptionsShapeCompiles() {
        Generate("""
            using System;
            using System.Collections.Generic;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [ConfigurationModel]
            public partial class DynamoDbOptions {
                [FromEnvironmentVariable("DYNAMODB_SERVICE_URL")]
                private string _serviceUrl = "";

                [FromEnvironmentVariable("AWS_REGION")]
                private string _region = "";

                private Dictionary<string, Func<IServiceProvider, object>> _clients = new();

                private Func<IServiceProvider, object>? _defaultClient;
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// The generated interface exposes a getter only. Documented as "a consumer cannot reach past the
    /// interface and mutate the model" — the setter lives on the class, for amenders.
    /// </summary>
    [Fact]
    public void TheInterfacePropertyIsReadOnlyWhileTheClassPropertyIsNot() {
        var result = GenerateModel("""
                private string _serviceUrl = "";
            """).AssertNoErrors();

        var onInterface = result.Compilation
            .GetTypeByMetadataName("TestApp.IServiceOptions")!
            .GetMembers("ServiceUrl")
            .OfType<IPropertySymbol>()
            .Single();

        var onClass = result.Compilation
            .GetTypeByMetadataName("TestApp.ServiceOptions")!
            .GetMembers("ServiceUrl")
            .OfType<IPropertySymbol>()
            .Single();

        Assert.Null(onInterface.SetMethod);
        Assert.NotNull(onClass.SetMethod);
    }

    /// <summary>Two models in one file each get their own interface and their own emitted file.</summary>
    [Fact]
    public void TwoModelsInOneFileEachGetTheirOwnInterface() {
        var result = Generate("""
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [ConfigurationModel]
            public partial class FirstOptions {
                private string _one = "";
            }

            [ConfigurationModel]
            public partial class SecondOptions {
                private string _two = "";
            }
            """).AssertNoErrors();

        Assert.NotNull(result.Compilation.GetTypeByMetadataName("TestApp.IFirstOptions"));
        Assert.NotNull(result.Compilation.GetTypeByMetadataName("TestApp.ISecondOptions"));
    }

    /// <summary>
    /// A class without <c>[ConfigurationModel]</c> is not a configuration model, however much it
    /// looks like one. The selector matching too broadly would generate an interface for every class
    /// in the assembly.
    /// </summary>
    [Fact]
    public void AClassWithoutTheAttributeGeneratesNothing() {
        var result = Generate("""
            namespace TestApp;

            public partial class NotAModel {
                private string _serviceUrl = "";
            }
            """).AssertNoErrors();

        Assert.Null(result.Compilation.GetTypeByMetadataName("TestApp.INotAModel"));
    }

    private static GeneratorResult GenerateModule(string models) =>
        Generate($$"""
            using System;
            using System.Collections.Generic;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestModule { }

            {{models}}
            """);

    /// <summary>
    /// Documented: "The generator collects every <c>[ConfigurationModel]</c> in an assembly into the
    /// module's generated <c>ConfigurationProvider</c>." That provider is the
    /// <c>IConfigurationPackage</c> <c>ConfigurationManager</c> reads, so if it is not emitted no
    /// model in the assembly resolves.
    /// </summary>
    [Fact]
    public void TheModuleGetsAConfigurationProviderPackage() {
        var result = GenerateModule("""
            [ConfigurationModel]
            public partial class ServiceOptions {
                private string _serviceUrl = "";
            }
            """).AssertNoErrors();

        var provider = result.Compilation.GetTypeByMetadataName("TestApp.TestModule+ConfigurationProvider");

        Assert.NotNull(provider);
        Assert.Contains(
            provider.AllInterfaces,
            symbol => symbol.Name == nameof(IConfigurationPackage));
    }

    /// <summary>
    /// The provider yields one <c>NewConfigurationValueProvider</c> per model, keyed on the generated
    /// interface, which is what makes <c>GetConfiguration&lt;IServiceOptions&gt;()</c> resolve rather
    /// than throw "is not a registered configuration type".
    /// </summary>
    [Fact]
    public void EveryModelInTheAssemblyIsRegisteredOnTheModule() {
        var result = GenerateModule("""
            [ConfigurationModel]
            public partial class FirstOptions {
                private string _one = "";
            }

            [ConfigurationModel]
            public partial class SecondOptions {
                private string _two = "";
            }
            """).AssertNoErrors();

        var configuration = result.SourceContaining("TestModule.Configuration");

        Assert.Contains("NewConfigurationValueProvider<IFirstOptions,FirstOptions>", WithoutWhitespace(configuration));
        Assert.Contains("NewConfigurationValueProvider<ISecondOptions,SecondOptions>", WithoutWhitespace(configuration));
    }

    /// <summary>
    /// A module with no configuration models still emits a provider, and it has to yield break rather
    /// than fall off the end of an iterator with a declared return type.
    /// </summary>
    [Fact]
    public void AModuleWithNoConfigurationModelsStillCompiles() {
        GenerateModule("").AssertNoErrors();
    }

    /// <summary>
    /// Documented: "<c>[FromEnvironmentVariable("NAME")]</c> reads the variable when the model is
    /// first constructed, falling back to the field's initialiser when the variable is unset or
    /// empty." The emitted read passes the current value as the default, which is what makes the
    /// fallback work — see <c>FromEnvironmentVariableTests</c> for the runtime half.
    /// </summary>
    [Fact]
    public void AnEnvironmentBackedFieldIsReadThroughTheEnvironmentWithTheFieldValueAsFallback() {
        var result = GenerateModule("""
            [ConfigurationModel]
            public partial class ServiceOptions {
                [FromEnvironmentVariable("SERVICE_URL")]
                private string _serviceUrl = "http://localhost";
            }
            """).AssertNoErrors();

        var configuration = result.SourceContaining("TestModule.Configuration");

        Assert.Contains(
            "model.ServiceUrl=environment.Value(\"SERVICE_URL\",model.ServiceUrl)!;",
            WithoutWhitespace(configuration));
    }

    /// <summary>
    /// A model with no environment-backed field gets <c>null</c> for its init action rather than an
    /// empty method, which is a different branch of the emitter.
    /// </summary>
    [Fact]
    public void AModelWithNoEnvironmentBackedFieldGetsNoInitialiser() {
        var result = GenerateModule("""
            [ConfigurationModel]
            public partial class ServiceOptions {
                private string _serviceUrl = "";
            }
            """).AssertNoErrors();

        var configuration = result.SourceContaining("TestModule.Configuration");

        Assert.Contains(
            "NewConfigurationValueProvider<IServiceOptions,ServiceOptions>(null)",
            WithoutWhitespace(configuration));
        Assert.DoesNotContain("ConfigureServiceOptions", configuration);
    }

    /// <summary>
    /// Field types the environment read has to convert to. The emitted assignment is the same shape
    /// for all of them, so a type the generator cannot express shows up here as a compile error.
    /// </summary>
    [Theory]
    [InlineData("string", "\"\"")]
    [InlineData("int", "0")]
    [InlineData("bool", "false")]
    [InlineData("double", "0")]
    [InlineData("long", "0")]
    public void AnEnvironmentBackedFieldOfAnyConvertibleTypeCompiles(string fieldType, string initialiser) {
        GenerateModule($$"""
            [ConfigurationModel]
            public partial class ServiceOptions {
                [FromEnvironmentVariable("SOME_VALUE")]
                private {{fieldType}} _someValue = {{initialiser}};
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// Both spellings of the attribute reach the emitter. The generator matches on the syntax name,
    /// so the suffixed form is a separate path from the short one.
    /// </summary>
    [Theory]
    [InlineData("FromEnvironmentVariable")]
    [InlineData("FromEnvironmentVariableAttribute")]
    public void AnEnvironmentBackedFieldIsRecognisedByEitherSpellingOfTheAttribute(string attribute) {
        var result = GenerateModule($$"""
            [ConfigurationModel]
            public partial class ServiceOptions {
                [{{attribute}}("SERVICE_URL")]
                private string _serviceUrl = "";
            }
            """).AssertNoErrors();

        Assert.Contains(
            "environment.Value(\"SERVICE_URL\",",
            WithoutWhitespace(result.SourceContaining("TestModule.Configuration")));
    }

    /// <summary>
    /// A hidden field is hidden everywhere: it gets no property, so an environment read written
    /// against it would not compile.
    /// </summary>
    [Fact]
    public void AHiddenFieldIsNotReadFromTheEnvironmentEvenWhenItSaysItShouldBe() {
        var result = GenerateModule("""
            [ConfigurationModel]
            public partial class ServiceOptions {
                [HideConfigurationField]
                [FromEnvironmentVariable("SERVICE_URL")]
                private string _serviceUrl = "";
            }
            """).AssertNoErrors();

        Assert.DoesNotContain("SERVICE_URL", result.SourceContaining("TestModule.Configuration"));
    }

    /// <summary>
    /// The module registers <c>IOptions&lt;IServiceOptions&gt;</c> as well as the package, which is
    /// the shape the documentation tells consumers to inject.
    /// </summary>
    [Fact]
    public void TheModuleRegistersIOptionsOfTheGeneratedInterface() {
        var result = GenerateModule("""
            [ConfigurationModel]
            public partial class ServiceOptions {
                private string _serviceUrl = "";
            }
            """).AssertNoErrors();

        Assert.Contains(
            "GetConfiguration<IServiceOptions>()",
            result.SourceContaining("TestModule.Configuration"));
    }
}
