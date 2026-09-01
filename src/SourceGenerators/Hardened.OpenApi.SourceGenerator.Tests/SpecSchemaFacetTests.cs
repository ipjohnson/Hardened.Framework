using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The constraint facets a specification-first body schema publishes.
/// </summary>
/// <remarks>
/// <para>
/// The model carried every one of these and the writer published none - only type, format and
/// description survived. Parameters travelled a different function that always knew the keywords,
/// which is how the trial's document-fidelity matrix came out inverted: every parameter constraint
/// published, every body constraint dropped, on both spec front ends at once, because this writer
/// is the one they share.
/// </para>
/// <para>
/// The assertion list is that matrix: bounds and their exclusive forms, lengths, pattern, item
/// counts, default, enum, and nullability as the 2020-12 type array.
/// </para>
/// </remarks>
public class SpecSchemaFacetTests {

    private static string Component(SchemaModel schema, params SchemaModel[] others) {
        var schemas = new List<SchemaModel> { schema };

        schemas.AddRange(others);

        var handlerSchema = SpecSchemaWriter.ForRef(
            "#/components/schemas/" + schema.Name, schemas);

        Assert.NotNull(handlerSchema);

        return handlerSchema!.Components.Single(c => c.Name == schema.Name).Json;
    }

    private static SchemaModel Thing(params PropertyModel[] properties) {
        var schema = new SchemaModel { Name = "Thing", Kind = SchemaKind.Object };

        schema.Properties.AddRange(properties);

        return schema;
    }

    [Fact]
    public void StringFacetsArePublished() {
        var schema = Component(Thing(new PropertyModel {
            Name = "code",
            Type = "string",
            MinLength = 1,
            MaxLength = 12,
            Pattern = "^[a-z]+$"
        }));

        Assert.Contains("\"minLength\":1", schema);
        Assert.Contains("\"maxLength\":12", schema);
        Assert.Contains("\"pattern\":\"^[a-z]+$\"", schema);
    }

    [Fact]
    public void NumericBoundsArePublished() {
        var schema = Component(Thing(new PropertyModel {
            Name = "rating",
            Type = "integer",
            Minimum = 1,
            Maximum = 5
        }));

        Assert.Contains("\"minimum\":1", schema);
        Assert.Contains("\"maximum\":5", schema);
    }

    /// <summary>
    /// The 2020-12 spelling, where the exclusive bound is itself the number - the same choice
    /// <c>JsonSchemaWriter</c> and <c>SchemaConstraintWriter</c> already made, because the default
    /// document is one this spelling is correct in.
    /// </summary>
    [Fact]
    public void ExclusiveBoundsUseTheModernSpelling() {
        var schema = Component(Thing(new PropertyModel {
            Name = "ratio",
            Type = "number",
            Minimum = 0,
            Maximum = 1,
            ExclusiveMinimum = true,
            ExclusiveMaximum = true
        }));

        Assert.Contains("\"exclusiveMinimum\":0", schema);
        Assert.Contains("\"exclusiveMaximum\":1", schema);
        Assert.DoesNotContain("\"exclusiveMinimum\":true", schema);
        Assert.DoesNotContain("\"minimum\"", schema);
    }

    [Fact]
    public void ADefaultAndAnInlineEnumArePublished() {
        var schema = Component(Thing(new PropertyModel {
            Name = "state",
            Type = "string",
            Default = "open",
            EnumValues = new List<string> { "open", "closed" }
        }));

        Assert.Contains("\"default\":\"open\"", schema);
        Assert.Contains("\"enum\":[\"open\",\"closed\"]", schema);
    }

    [Fact]
    public void ANullablePropertyPublishesTheTypeArray() {
        var schema = Component(Thing(new PropertyModel {
            Name = "deliveredAt",
            Type = "string",
            Format = "date-time",
            IsNullable = true
        }));

        Assert.Contains("\"deliveredAt\":{\"type\":[\"string\",\"null\"]", schema);
    }

    [Fact]
    public void AnArrayPropertyPublishesItsItemBounds() {
        var schema = Component(Thing(new PropertyModel {
            Name = "lines",
            Type = "array",
            IsArray = true,
            ArrayItemsType = "string",
            MinItems = 1,
            MaxItems = 10
        }));

        Assert.Contains("\"minItems\":1", schema);
        Assert.Contains("\"maxItems\":10", schema);
    }

    [Fact]
    public void ANamedArraySchemaPublishesItsOwnBounds() {
        var array = new SchemaModel {
            Name = "Batch",
            Kind = SchemaKind.Array,
            ArrayItemsType = "string",
            MinItems = 1,
            MaxItems = 100
        };

        var schema = Component(array);

        Assert.Contains("\"type\":\"array\"", schema);
        Assert.Contains("\"minItems\":1", schema);
        Assert.Contains("\"maxItems\":100", schema);
    }

    /// <summary>A reference stays a bare reference; the component it names carries the facts.</summary>
    [Fact]
    public void AReferencePropertyIsLeftAlone() {
        var owner = Thing(new PropertyModel {
            Name = "address",
            Ref = "#/components/schemas/Address",
            IsNullable = true,
            MinLength = 3
        });

        var address = new SchemaModel { Name = "Address", Kind = SchemaKind.Object };

        var schema = Component(owner, address);

        Assert.Contains("\"address\":{\"$ref\":\"#/components/schemas/Address\"}", schema);
    }
}
