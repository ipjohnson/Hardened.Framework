using Hardened.SourceGeneration.Testing;
using Xunit;
using Hardened.Idl;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A specification's <c>default</c> reaching the generated code.
/// </summary>
/// <remarks>
/// It was never read. Every optional parameter and property got <c>= default</c> whatever the
/// document said, so a spec declaring <c>default: 25</c> produced a handler that saw null.
/// </remarks>
public class DefaultValueTests {

    [Theory]
    [InlineData("25", "int", "25")]
    [InlineData("asc", "string", "\"asc\"")]
    [InlineData("true", "bool", "true")]
    [InlineData("false", "bool", "false")]
    [InlineData("0.5", "double", "0.5")]
    [InlineData("0.5", "float", "0.5f")]
    [InlineData("1.25", "decimal", "1.25m")]
    public void ValuesWithAConstantFormAreRendered(string value, string csType, string expected) {
        Assert.Equal(expected, DefaultLiteral.Format(value, csType));
    }

    [Fact]
    public void StringsAreEscaped() {
        Assert.Equal("\"a \\\"quoted\\\" value\"", DefaultLiteral.Format("a \"quoted\" value", "string"));
    }

    /// <summary>
    /// C# requires an optional parameter's default to be a compile-time constant, so a date has no
    /// representation whatever the spec says — a constructor call is not a constant.
    /// </summary>
    [Theory]
    [InlineData("DateTime")]
    [InlineData("DateOnly")]
    [InlineData("byte[]")]
    public void TypesWithNoConstantFormAreDeclined(string csType) {
        Assert.Null(DefaultLiteral.Format("2020-01-01T00:00:00Z", csType));
    }

    /// <summary>A value that does not parse as its declared type is declined rather than emitted.</summary>
    [Fact]
    public void AValueThatDoesNotFitItsTypeIsDeclined() {
        Assert.Null(DefaultLiteral.Format("not-a-number", "int"));
        Assert.Null(DefaultLiteral.Format("maybe", "bool"));
    }

    [Fact]
    public void NoDeclaredDefaultRendersNothing() {
        Assert.Null(DefaultLiteral.Format(null, "int"));
    }

    [Fact]
    public void RecordPropertiesCarryTheirDeclaredDefaults() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredDefaults).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("""string? Label = "unnamed" """.TrimEnd(), generated);
        Assert.Contains("int? Size = 10", generated);
        Assert.Contains("double? Ratio = 0.5", generated);
        Assert.Contains("bool? Enabled = true", generated);
    }

    /// <summary>
    /// A date default falls back to the type's own default rather than emitting something that
    /// does not compile.
    /// </summary>
    [Fact]
    public void ADefaultWithNoConstantFormFallsBack() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredDefaults).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("DateTime? Since = default", generated);
    }

    /// <summary>
    /// The binder is where a declared default actually changes behaviour: an absent query value
    /// arrives as the specification's default rather than as null.
    /// </summary>
    [Fact]
    public void TheBinderParsesQueryParametersWithTheirDeclaredDefaults() {
        var result = OpenApiGenerator.Run(Specs.DeclaredDefaults).AssertNoErrors();

        var handler = result.SourceContaining("ThingController_ListThings");

        Assert.Contains("ParseWithDefault", handler);
        Assert.Contains("25", handler);
        Assert.Contains("\"asc\"", handler);
    }
}
