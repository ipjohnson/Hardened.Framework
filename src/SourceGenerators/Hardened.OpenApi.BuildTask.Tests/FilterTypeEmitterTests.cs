using System.Collections.Generic;
using Hardened.Idl.Emitters;
using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The partial attribute class an <c>x-filter-types</c> entry becomes.
/// </summary>
/// <remarks>
/// <para>
/// The emitter was at <b>0% line coverage</b> — not one line had ever run. It emits a public
/// attribute that a consumer completes with the other half of the partial, so what it gets wrong is
/// wrong in code somebody else has to compile against.
/// </para>
/// <para>
/// Default values are the sharp part: <c>FormatDefault</c> switches on the declared C# type and
/// writes a literal, so a string default has to be quoted and escaped, a <c>bool</c> has to be
/// lower-cased for C#, and an enum default has to be qualified by its type rather than quoted.
/// </para>
/// </remarks>
public class FilterTypeEmitterTests {

    private const string FilterNamespace = EmitterHarness.RootNamespace + ".Filters";

    private static FilterTypeModel Model(string name = "throttle", params FilterTypePropertyModel[] properties) =>
        new() {
            Name = name,
            Namespace = FilterNamespace,
            Properties = new List<FilterTypePropertyModel>(properties)
        };

    private static string Emit(FilterTypeModel model) =>
        EmitterHarness.Write(ns => FilterTypeEmitter.Emit(ns, model), FilterNamespace);

    #region the type itself

    /// <summary>
    /// Public and partial, because the consumer writes the other half — the interface
    /// implementation that makes it an actual filter.
    /// </summary>
    [Fact]
    public void TheAttributeIsPublicAndPartial() {
        Assert.Contains("public partial class ThrottleAttribute", Emit(Model()));
    }

    [Fact]
    public void TheAttributeDerivesFromAttribute() {
        Assert.Contains("Attribute", Emit(Model()));
    }

    /// <summary>
    /// Applicable to a class and a method, which is what lets one filter be declared for a whole
    /// controller or for a single operation.
    /// </summary>
    [Fact]
    public void TheAttributeIsUsableOnAClassAndAMethod() {
        var output = Emit(Model());

        Assert.Contains("AttributeTargets.Class", output);
        Assert.Contains("AttributeTargets.Method", output);
    }

    /// <summary>
    /// The class name comes from the model, which appends the suffix — so a spec naming a filter
    /// <c>throttle</c> produces <c>[Throttle]</c> at the use site.
    /// </summary>
    [Theory]
    [InlineData("throttle", "ThrottleAttribute")]
    [InlineData("rate_limit", "RateLimitAttribute")]
    [InlineData("require-scope", "RequireScopeAttribute")]
    public void TheClassNameIsPascalCasedWithTheSuffix(string declared, string expected) {
        Assert.Contains($"public partial class {expected}", Emit(Model(declared)));
    }

    [Fact]
    public void AFilterWithNoPropertiesStillEmitsTheType() {
        Assert.Contains("public partial class ThrottleAttribute", Emit(Model()));
    }

    #endregion

    #region properties

    [Fact]
    public void ADeclaredPropertyIsEmittedPublic() {
        var output = Emit(Model("throttle",
            new FilterTypePropertyModel { Name = "Limit", CSharpType = "int" }));

        Assert.Contains("public int Limit", output);
    }

    [Fact]
    public void EveryDeclaredPropertyIsEmitted() {
        var output = Emit(Model("throttle",
            new FilterTypePropertyModel { Name = "Limit", CSharpType = "int" },
            new FilterTypePropertyModel { Name = "Window", CSharpType = "string" }));

        Assert.Contains("public int Limit", output);
        Assert.Contains("public string Window", output);
    }

    [Fact]
    public void APropertyWithNoDefaultGetsNoInitializer() {
        var output = Emit(Model("throttle",
            new FilterTypePropertyModel { Name = "Limit", CSharpType = "int" }));

        Assert.DoesNotContain("Limit { get; set; } =", output);
    }

    #endregion

    #region default values

    [Fact]
    public void AnIntegerDefaultIsWrittenBare() {
        Assert.Contains(
            "= 100",
            Emit(Model("throttle",
                new FilterTypePropertyModel { Name = "Limit", CSharpType = "int", Default = "100" })));
    }

    [Theory]
    [InlineData("long", "9000")]
    [InlineData("float", "1.5")]
    [InlineData("double", "2.25")]
    public void EveryNumericDefaultIsWrittenBare(string csharpType, string value) {
        Assert.Contains(
            $"= {value}",
            Emit(Model("throttle",
                new FilterTypePropertyModel { Name = "Value", CSharpType = csharpType, Default = value })));
    }

    [Fact]
    public void AStringDefaultIsQuoted() {
        Assert.Contains(
            "= \"minute\"",
            Emit(Model("throttle",
                new FilterTypePropertyModel { Name = "Window", CSharpType = "string", Default = "minute" })));
    }

    /// <summary>
    /// A default containing a quote or a backslash has to survive being written into a C# literal.
    /// </summary>
    [Theory]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("back\\slash", "\"back\\\\slash\"")]
    public void AStringDefaultIsEscaped(string declared, string expected) {
        Assert.Contains(
            "= " + expected,
            Emit(Model("throttle",
                new FilterTypePropertyModel { Name = "Window", CSharpType = "string", Default = declared })));
    }

    /// <summary>
    /// YAML writes <c>True</c>; C# will not compile it.
    /// </summary>
    [Theory]
    [InlineData("True", "true")]
    [InlineData("true", "true")]
    [InlineData("False", "false")]
    [InlineData("FALSE", "false")]
    public void ABooleanDefaultIsLowerCasedForCSharp(string declared, string expected) {
        var output = Emit(Model("throttle",
            new FilterTypePropertyModel { Name = "Enabled", CSharpType = "bool", Default = declared }));

        Assert.Contains("= " + expected, output);
    }

    /// <summary>
    /// An enum default is qualified by its type rather than quoted — the property's type is the
    /// enum, so a string literal would not compile.
    /// </summary>
    [Fact]
    public void AnEnumDefaultIsQualifiedByItsType() {
        var output = Emit(Model("throttle",
            new FilterTypePropertyModel {
                Name = "Mode",
                CSharpType = "string",
                EnumType = "Test.Api.Filters.ThrottleMode",
                Default = "Sliding"
            }));

        Assert.Contains("= Test.Api.Filters.ThrottleMode.Sliding", output);
    }

    /// <summary>
    /// The enum type also becomes the property's type, not the declared <c>CSharpType</c>.
    /// </summary>
    [Fact]
    public void AnEnumPropertyUsesTheEnumTypeRatherThanTheDeclaredOne() {
        var output = Emit(Model("throttle",
            new FilterTypePropertyModel {
                Name = "Mode",
                CSharpType = "string",
                EnumType = "Test.Api.Filters.ThrottleMode"
            }));

        Assert.Contains("ThrottleMode Mode", output);
        Assert.DoesNotContain("string Mode", output);
    }

    /// <summary>
    /// An unrecognised C# type falls back to a quoted literal rather than emitting the value bare,
    /// which would produce something that does not compile.
    /// </summary>
    [Fact]
    public void AnUnrecognisedTypeFallsBackToAQuotedLiteral() {
        Assert.Contains(
            "= \"PT1M\"",
            Emit(Model("throttle",
                new FilterTypePropertyModel {
                    Name = "Window", CSharpType = "TimeSpan", Default = "PT1M"
                })));
    }

    #endregion
}
