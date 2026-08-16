using Hardened.Idl.Models;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <c>minProperties</c> and <c>maxProperties</c>, which bound how many entries an object carries.
/// </summary>
/// <remarks>
/// They constrain the same thing <c>minItems</c>/<c>maxItems</c> do for an array — a count — and a
/// schema that becomes a <c>Dictionary&lt;string, T&gt;</c> validates through the same
/// <c>[ItemCount]</c>, which emits <c>.Count</c> bounds either way. Carried on the same model
/// fields rather than their own, because a schema is an object or an array and never both, so the
/// two pairs cannot apply to one property.
/// </remarks>
public class PropertyCountTests {

    private static PropertyModel Property(string name) {
        var model = OpenApiSpecParser.Parse(Specs.PropertyCountBounds, "test", CancellationToken.None);

        Assert.NotNull(model);

        return model!.Schemas.First(s => s.Name == "Thing").Properties.First(p => p.Name == name);
    }

    [Fact]
    public void PropertyCountBoundsAreRead() {
        var labels = Property("labels");

        Assert.True(labels.IsDictionary);
        Assert.Equal(1, labels.MinItems);
        Assert.Equal(10, labels.MaxItems);
    }

    /// <summary>An array's own bounds are unaffected.</summary>
    [Fact]
    public void ArrayBoundsStillWork() {
        var tags = Property("tags");

        Assert.True(tags.IsArray);
        Assert.Equal(2, tags.MinItems);
        Assert.Equal(5, tags.MaxItems);
    }

    /// <summary>
    /// The bounds reach the generated record as an <c>[ItemCount]</c>, which is what actually
    /// enforces them.
    /// </summary>
    [Fact]
    public void TheBoundsBecomeAnItemCountConstraint() {
        var generated = OpenApiGenerator.Run(Specs.PropertyCountBounds).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        var record = generated.Split('\n').First(l => l.Contains("record Thing("));

        Assert.Contains("ItemCount(Min = 1, Max = 10)", record);
        Assert.Contains("ItemCount(Min = 2, Max = 5)", record);
    }

    /// <summary>
    /// And a validator is generated for the type, so the bounds are enforced rather than merely
    /// declared.
    /// </summary>
    [Fact]
    public void AConstrainedDictionaryProducesAValidator() {
        var result = OpenApiGenerator.Run(Specs.PropertyCountBounds).AssertNoErrors();

        Assert.Contains("ValidateAttribute<", result.SourceContaining("ThingController_CreateThing"));
    }
}
