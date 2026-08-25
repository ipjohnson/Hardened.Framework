using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Idl.Validation;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// OpenAPI's constraint keywords becoming ValidationModules attributes.
/// </summary>
/// <remarks>
/// <para>
/// At <b>48% line coverage</b>, and what was dark is the half that matters: every guard here exists
/// because a real published specification produced code that did not compile. The doc comments name
/// them — Box puts <c>minimum</c> on a string, Vercel puts an <c>enum</c> on a member that fell back
/// to <c>JsonElement</c>, OpenAI types parameters as generated enums. Each one is a CS0019 or CS0037
/// in a generated file, from a spec that is not wrong.
/// </para>
/// <para>
/// So these tests are mostly about what is <em>not</em> emitted. A guard that stops firing does not
/// fail a test that only checks the happy path — it fails a consumer's build.
/// </para>
/// </remarks>
public class ConstraintAttributesTests {

    private static PatternRegistry Patterns() =>
        new(EmitterHarness.RootNamespace + ".Validation", "petstore");

    private static PropertyModel Property(
        int? minLength = null, int? maxLength = null,
        decimal? minimum = null, decimal? maximum = null,
        bool exclusiveMinimum = false, bool exclusiveMaximum = false,
        string? pattern = null,
        int? minItems = null, int? maxItems = null,
        List<string>? enumValues = null) =>
        new() {
            Name = "value",
            MinLength = minLength,
            MaxLength = maxLength,
            Minimum = minimum,
            Maximum = maximum,
            ExclusiveMinimum = exclusiveMinimum,
            ExclusiveMaximum = exclusiveMaximum,
            Pattern = pattern,
            MinItems = minItems,
            MaxItems = maxItems,
            EnumValues = enumValues
        };

    private static IReadOnlyList<ConstraintAttributes.Model> For(
        PropertyModel property, string csType, bool required = false) =>
        ConstraintAttributes.ForProperty(property, required, Patterns(), csType);

    private static IReadOnlyList<string> Names(IReadOnlyList<ConstraintAttributes.Model> attributes) =>
        attributes.Select(attribute => attribute.Type.Name).ToList();

    private static ConstraintAttributes.Model Single(
        IReadOnlyList<ConstraintAttributes.Model> attributes, string name) =>
        Assert.Single(attributes, attribute => attribute.Type.Name == name);

    #region required

    [Fact]
    public void RequiredIsEmittedWhenTheCallerSaysSo() {
        Assert.Contains("RequiredAttribute", Names(For(Property(), "string", required: true)));
    }

    [Fact]
    public void RequiredIsNotEmittedOtherwise() {
        Assert.DoesNotContain("RequiredAttribute", Names(For(Property(), "string")));
    }

    [Fact]
    public void RequiredTakesNoArguments() {
        Assert.Empty(Single(For(Property(), "string", required: true), "RequiredAttribute").Arguments);
    }

    /// <summary>
    /// Requiredness comes from the caller, not the facets — the caller is the one that knows the
    /// C# type. A non-nullable value type can never be absent, and the validation generator answers
    /// <c>[Required]</c> on one with <c>value.petId is null</c> against a <c>long</c>: CS0037.
    /// </summary>
    [Fact]
    public void TheFacetsDoNotDecideRequiredness() {
        Assert.DoesNotContain("RequiredAttribute", Names(For(Property(minLength: 1), "long")));
    }

    #endregion

    #region string length

    [Fact]
    public void AStringWithBothLengthBoundsGetsStringLength() {
        Assert.Equal(
            ["Min = 1", "Max = 64"],
            Single(For(Property(minLength: 1, maxLength: 64), "string"), "StringLengthAttribute").Arguments);
    }

    /// <summary>
    /// Named arguments, because a spec may set one bound and not the other while the positional
    /// constructor takes both.
    /// </summary>
    [Fact]
    public void OnlyTheDeclaredBoundIsWritten() {
        Assert.Equal(
            ["Max = 64"],
            Single(For(Property(maxLength: 64), "string"), "StringLengthAttribute").Arguments);

        Assert.Equal(
            ["Min = 1"],
            Single(For(Property(minLength: 1), "string"), "StringLengthAttribute").Arguments);
    }

    [Fact]
    public void NoLengthBoundEmitsNoStringLength() {
        Assert.DoesNotContain("StringLengthAttribute", Names(For(Property(), "string")));
    }

    /// <summary>
    /// A length cannot be read off a type that has none.
    /// </summary>
    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("JsonElement")]
    [InlineData("List<string>")]
    public void ALengthBoundOnANonStringTypeIsDropped(string csType) {
        Assert.DoesNotContain(
            "StringLengthAttribute", Names(For(Property(minLength: 1, maxLength: 64), csType)));
    }

    #endregion

    #region numeric range

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("double")]
    [InlineData("decimal")]
    [InlineData("float")]
    [InlineData("short")]
    [InlineData("byte")]
    public void ANumericTypeWithBoundsGetsRange(string csType) {
        Assert.Contains("RangeAttribute", Names(For(Property(minimum: 1, maximum: 10), csType)));
    }

    /// <summary>
    /// Box declares <c>minimum</c> on a string. Emitted, the generated comparison is CS0019.
    /// </summary>
    [Theory]
    [InlineData("string")]
    [InlineData("JsonElement")]
    [InlineData("List<int>")]
    public void ANumericBoundOnANonNumericTypeIsDropped(string csType) {
        Assert.DoesNotContain("RangeAttribute", Names(For(Property(minimum: 1, maximum: 10), csType)));
    }

    /// <summary>
    /// <c>Range</c> has no partially-bounded constructor, so an absent bound becomes the extreme
    /// rather than being omitted.
    /// </summary>
    [Fact]
    public void AnAbsentBoundBecomesTheExtreme() {
        var arguments = Single(For(Property(minimum: 5), "int"), "RangeAttribute").Arguments;

        Assert.Equal(2, arguments.Count);
        Assert.Equal("5", arguments[0]);
        Assert.Equal(((double)decimal.MaxValue).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            arguments[1]);
    }

    [Fact]
    public void ExclusiveBoundsAreNamedArgumentsAfterThePositionalOnes() {
        var arguments =
            Single(For(Property(minimum: 1, maximum: 10, exclusiveMinimum: true, exclusiveMaximum: true), "int"),
                "RangeAttribute").Arguments;

        Assert.Equal(4, arguments.Count);
        Assert.Equal("ExclusiveMin = true", arguments[2]);
        Assert.Equal("ExclusiveMax = true", arguments[3]);
    }

    [Fact]
    public void AnInclusiveBoundWritesNoExclusiveFlag() {
        var arguments = Single(For(Property(minimum: 1, maximum: 10), "int"), "RangeAttribute").Arguments;

        Assert.Equal(2, arguments.Count);
    }

    /// <summary>
    /// Invariant culture, or a machine with a comma decimal separator emits <c>1,5</c> and the
    /// generated file does not parse.
    /// </summary>
    [Fact]
    public void AFractionalBoundIsWrittenInvariant() {
        Assert.Equal(
            "1.5",
            Single(For(Property(minimum: 1.5m, maximum: 10), "double"), "RangeAttribute").Arguments[0]);
    }

    #endregion

    #region pattern

    [Fact]
    public void AStringWithAPatternGetsPatternInItsReferenceForm() {
        var arguments = Single(For(Property(pattern: "^[a-z]+$"), "string"), "PatternAttribute").Arguments;

        Assert.Equal(2, arguments.Count);
        Assert.StartsWith("typeof(global::", arguments[0]);
        Assert.StartsWith("nameof(global::", arguments[1]);
    }

    /// <summary>
    /// A pattern .NET's engine will not take is reported against the spec rather than emitted into
    /// a member that cannot be generated.
    /// </summary>
    [Fact]
    public void APatternTheRuntimeRefusesEmitsNoAttribute() {
        Assert.DoesNotContain(
            "PatternAttribute", Names(For(Property(pattern: @"^[a-zA-Z0-9\-\_]+$"), "string")));
    }

    [Theory]
    [InlineData("int")]
    [InlineData("JsonElement")]
    public void APatternOnANonStringTypeIsDropped(string csType) {
        Assert.DoesNotContain("PatternAttribute", Names(For(Property(pattern: "^[a-z]+$"), csType)));
    }

    [Fact]
    public void AnEmptyPatternEmitsNothing() {
        Assert.DoesNotContain("PatternAttribute", Names(For(Property(pattern: ""), "string")));
    }

    #endregion

    #region item count

    [Theory]
    [InlineData("List<string>")]
    [InlineData("Dictionary<string, int>")]
    [InlineData("string[]")]
    public void ACountedTypeWithBoundsGetsItemCount(string csType) {
        Assert.Equal(
            ["Min = 1", "Max = 5"],
            Single(For(Property(minItems: 1, maxItems: 5), csType), "ItemCountAttribute").Arguments);
    }

    /// <summary>
    /// An array whose element type cannot be named degrades to <c>JsonElement</c>, and
    /// <c>[ItemCount]</c> then draws <c>value.X.Count</c> against a struct with no such member.
    /// </summary>
    [Theory]
    [InlineData("JsonElement")]
    [InlineData("string")]
    [InlineData("int")]
    public void AnItemBoundOnATypeWithNoCountIsDropped(string csType) {
        Assert.DoesNotContain("ItemCountAttribute", Names(For(Property(minItems: 1, maxItems: 5), csType)));
    }

    #endregion

    #region allowed values

    [Fact]
    public void AStringEnumBecomesAllowedValues() {
        Assert.Equal(
            ["\"available\"", "\"pending\""],
            Single(For(Property(enumValues: ["available", "pending"]), "string"), "AllowedValuesAttribute")
                .Arguments);
    }

    /// <summary>
    /// A parameter whose schema is a <c>$ref</c> to an enum is typed as the generated enum, and
    /// <c>[AllowedValues]</c> then compares that enum against string literals — CS0019, once per
    /// member. OpenAI's specification does this.
    /// </summary>
    [Theory]
    [InlineData("PetStatus")]
    [InlineData("JsonElement")]
    [InlineData("int")]
    public void AnEnumOnANonStringTypeIsDropped(string csType) {
        Assert.DoesNotContain(
            "AllowedValuesAttribute", Names(For(Property(enumValues: ["available"]), csType)));
    }

    [Fact]
    public void AnEmptyEnumEmitsNothing() {
        Assert.DoesNotContain("AllowedValuesAttribute", Names(For(Property(enumValues: []), "string")));
    }

    [Fact]
    public void AnAllowedValueContainingAQuoteIsEscaped() {
        Assert.Equal(
            ["\"say \\\"hi\\\"\"", "\"back\\\\slash\""],
            Single(For(Property(enumValues: ["say \"hi\"", "back\\slash"]), "string"),
                "AllowedValuesAttribute").Arguments);
    }

    #endregion

    [Fact]
    public void APropertyWithNoConstraintsGetsNoAttributes() {
        Assert.Empty(For(Property(), "string"));
    }

    [Fact]
    public void EveryAttributeComesFromTheConstraintsNamespace() {
        var attributes = For(
            Property(minLength: 1, maxLength: 5, pattern: "^[a-z]+$", enumValues: ["a"]),
            "string", required: true);

        Assert.NotEmpty(attributes);
        Assert.All(attributes,
            attribute => Assert.Equal("ValidationModules.Constraints", attribute.Type.Namespace));
    }

    [Fact]
    public void ValidateNestedNamesTheNestedAttribute() {
        Assert.Equal("ValidateNestedAttribute", ConstraintAttributes.ValidateNested().Name);
        Assert.Equal("ValidationModules.Constraints", ConstraintAttributes.ValidateNested().Namespace);
    }

    /// <summary>
    /// A parameter goes through the same builder as a property, so the guards cannot be enforced on
    /// request bodies and skipped on query strings — the failure the shared facets interface was
    /// introduced to prevent.
    /// </summary>
    [Fact]
    public void AParameterIsBuiltByTheSameRules() {
        var parameter = new ParameterModel { Name = "status", MinLength = 1, MaxLength = 8 };

        Assert.Equal(
            ["Min = 1", "Max = 8"],
            Single(
                ConstraintAttributes.ForParameter(parameter, false, "string", Patterns()),
                "StringLengthAttribute").Arguments);
    }

    [Fact]
    public void AParameterBoundOnANonStringTypeIsDroppedToo() {
        var parameter = new ParameterModel { Name = "limit", MinLength = 1, MaxLength = 8 };

        Assert.Empty(ConstraintAttributes.ForParameter(parameter, false, "int", Patterns()));
    }
}
