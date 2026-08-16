using Hardened.Idl.Models;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <c>required</c> and <c>nullable</c> as the separate axes they are.
/// </summary>
/// <remarks>
/// One flag used to carry both, so <c>nullable</c> was never read at all: a required property was
/// non-nullable and an optional one was nullable-with-a-default, and a spec saying a value must be
/// present but may be null had no way to say so.
/// </remarks>
public class NullabilityTests {

    private static ServiceSpecModel Parse() {
        var model = OpenApiSpecParser.Parse(Specs.RequiredAndNullable, "test", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static PropertyModel Property(string name) =>
        Parse().Schemas.First(s => s.Name == "Thing").Properties.First(p => p.Name == name);

    [Fact]
    public void RequiredAndNotNullableIsNeitherNullableNorDefaulted() {
        var property = Property("requiredPlain");

        Assert.True(property.IsRequired);
        Assert.False(property.IsNullable);
        Assert.False(property.IsCSharpNullable);
        Assert.False(property.HasDefault);
        Assert.True(property.ConstrainedAsRequired);
    }

    /// <summary>
    /// The combination that had no representation: the value must be supplied, and may be null.
    /// Nullable in C#, but with no default — and no <c>[Required]</c>, which rejects null.
    /// </summary>
    [Fact]
    public void RequiredAndNullableIsNullableWithoutADefault() {
        var property = Property("requiredNullable");

        Assert.True(property.IsRequired);
        Assert.True(property.IsNullable);
        Assert.True(property.IsCSharpNullable);
        Assert.False(property.HasDefault);
        Assert.False(property.ConstrainedAsRequired);
    }

    [Fact]
    public void OptionalIsNullableAndDefaultedWhateverNullableSays() {
        foreach (var name in new[] { "optionalPlain", "optionalNullable" }) {
            var property = Property(name);

            Assert.False(property.IsRequired);
            Assert.True(property.IsCSharpNullable);
            Assert.True(property.HasDefault);
            Assert.False(property.ConstrainedAsRequired);
        }
    }

    [Fact]
    public void ParametersFollowTheSameRules() {
        var parameters = Parse().Services.First(s => s.Tag == "Thing").Operations.Single().Parameters;

        var requiredNullable = parameters.First(p => p.Name == "requiredNullable");

        Assert.True(requiredNullable.IsRequired);
        Assert.True(requiredNullable.IsNullable);
        Assert.True(requiredNullable.IsCSharpNullable);
        Assert.False(requiredNullable.HasDefault);
    }

    /// <summary>
    /// What it all amounts to in the generated record: a required-and-nullable property is
    /// <c>string?</c>, sits in the leading group because it has no default, and carries no
    /// <c>[Required]</c>.
    /// </summary>
    [Fact]
    public void TheGeneratedRecordReflectsBothAxes() {
        var result = OpenApiGenerator.Run(Specs.RequiredAndNullable).AssertNoErrors();

        var generated = result.SourceContaining("petstore.g.cs");

        Assert.Contains("string RequiredPlain", generated);
        Assert.Contains("string? RequiredNullable", generated);
        Assert.Contains("string? OptionalPlain = default", generated);

        // [Required] would reject the null the spec permits.
        var record = generated.Split('\n').First(l => l.Contains("record Thing("));

        Assert.DoesNotContain("Required] string? RequiredNullable", record);
    }
}
