using System.Collections.Generic;
using System.Text.Json;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Xunit;
using Hardened.Web.Runtime.Responses;

namespace Hardened.SourceGenerator.Tests.SpecBridge;

/// <summary>
/// The OpenAPI schema written from the normalised model rather than from a type symbol.
/// </summary>
/// <remarks>
/// <c>JsonSchemaWriter</c> walks an <c>ITypeSymbol</c> and cannot serve the specification-first
/// path, whose payload types are written by the build task rather than declared in the consumer's
/// source. Without this the published document carried paths and operation ids and no
/// <c>components</c> at all.
/// </remarks>
public class SpecSchemaWriterTests {

    private static SchemaModel Object(string name, params PropertyModel[] properties) {
        var schema = new SchemaModel { Name = name, Kind = SchemaKind.Object };

        schema.Properties.AddRange(properties);

        return schema;
    }

    private static PropertyModel Property(
        string name, string? type = "string", string? reference = null,
        string? description = null, bool required = false, string? headerName = null,
        int? messagePackIndex = null) =>
        new() {
            Name = name, Type = reference == null ? type : null, Ref = reference,
            Description = description, IsRequired = required, HeaderName = headerName,
            MessagePackIndex = messagePackIndex
        };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement Component(HandlerSchema schema, string name) {
        foreach (var component in schema.Components) {
            if (component.Name == name) {
                return Parse(component.Json);
            }
        }

        throw new Xunit.Sdk.XunitException($"no component named {name}");
    }

    /// <summary>
    /// The key the contract stated, published so a client generator can read it.
    /// </summary>
    /// <remarks>
    /// Without this the document describes a MessagePack representation whose field identity is
    /// invisible, and a client generated from it agrees with the server about property names and
    /// nothing else - which under a keyed format is agreement about the wrong thing.
    /// </remarks>
    [Fact]
    public void AKeyedPropertyPublishesItsIndex() {
        var schemas = new List<SchemaModel> {
            Object("Reading", Property("sensor", messagePackIndex: 0), Property("value", messagePackIndex: 7))
        };

        var reading = Component(SpecSchemaWriter.ForRef("#/components/schemas/Reading", schemas)!, "Reading");
        var properties = reading.GetProperty("properties");

        Assert.Equal(0, properties.GetProperty("sensor").GetProperty("x-message-pack-index").GetInt32());
        Assert.Equal(7, properties.GetProperty("value").GetProperty("x-message-pack-index").GetInt32());
    }

    /// <summary>
    /// An unkeyed property publishes nothing, so a contract that never mentioned MessagePack
    /// produces the document it always did.
    /// </summary>
    [Fact]
    public void AnUnkeyedPropertyPublishesNoExtension() {
        var schemas = new List<SchemaModel> { Object("Pet", Property("id")) };

        var pet = Component(SpecSchemaWriter.ForRef("#/components/schemas/Pet", schemas)!, "Pet");

        Assert.False(
            pet.GetProperty("properties").GetProperty("id")
                .TryGetProperty("x-message-pack-index", out _));
    }

    /// <summary>
    /// A keyed reference is wrapped, for the reason a described one is: a <c>$ref</c> takes no
    /// siblings in 3.0, so the key written beside it would be dropped by every reader.
    /// </summary>
    [Fact]
    public void AKeyedReferenceIsWrappedInAllOf() {
        var schemas = new List<SchemaModel> {
            Object("Order", Property("pet", reference: "#/components/schemas/Pet", messagePackIndex: 3)),
            Object("Pet", Property("id"))
        };

        var order = Component(SpecSchemaWriter.ForRef("#/components/schemas/Order", schemas)!, "Order");
        var pet = order.GetProperty("properties").GetProperty("pet");

        Assert.Equal(3, pet.GetProperty("x-message-pack-index").GetInt32());
        Assert.Equal(
            "#/components/schemas/Pet",
            pet.GetProperty("allOf")[0].GetProperty("$ref").GetString());
    }

    /// <summary>
    /// An array member carries its key too. It is one member of the object whatever its type, and
    /// a keyed object needs an answer for every member.
    /// </summary>
    [Fact]
    public void AKeyedArrayMemberPublishesItsIndex() {
        var schema = Object("Reading");

        schema.Properties.Add(new PropertyModel {
            Name = "tags", IsArray = true, ArrayItemsType = "string", MessagePackIndex = 2
        });

        var reading = Component(
            SpecSchemaWriter.ForRef("#/components/schemas/Reading", new List<SchemaModel> { schema })!,
            "Reading");

        Assert.Equal(
            2,
            reading.GetProperty("properties").GetProperty("tags")
                .GetProperty("x-message-pack-index").GetInt32());
    }

    [Fact]
    public void ANullRefWritesNothing() {
        Assert.Null(SpecSchemaWriter.ForRef(null, new List<SchemaModel>()));
        Assert.Null(SpecSchemaWriter.ForArrayOf(null, new List<SchemaModel>()));
    }

    [Fact]
    public void ARefThatNamesNoSchemaWritesNothing() =>
        Assert.Null(SpecSchemaWriter.ForRef("#/components/schemas/Missing", new List<SchemaModel>()));

    [Fact]
    public void TheRootIsAReferenceAndTheSchemaIsAComponent() {
        var schemas = new List<SchemaModel> { Object("Pet", Property("id"), Property("name")) };

        var written = SpecSchemaWriter.ForRef("#/components/schemas/Pet", schemas)!;

        Assert.Equal("#/components/schemas/Pet", Parse(written.Schema).GetProperty("$ref").GetString());

        var pet = Component(written, "Pet");

        Assert.Equal("object", pet.GetProperty("type").GetString());
        Assert.True(pet.GetProperty("properties").TryGetProperty("id", out _));
        Assert.True(pet.GetProperty("properties").TryGetProperty("name", out _));
    }

    [Fact]
    public void AnArrayWrapsTheReference() {
        var schemas = new List<SchemaModel> { Object("Pet", Property("id")) };

        var written = SpecSchemaWriter.ForArrayOf("#/components/schemas/Pet", schemas)!;
        var root = Parse(written.Schema);

        Assert.Equal("array", root.GetProperty("type").GetString());
        Assert.Equal(
            "#/components/schemas/Pet",
            root.GetProperty("items").GetProperty("$ref").GetString());
    }

    [Fact]
    public void DescriptionsReachTheSchemaAndItsProperties() {
        var pet = Object("Pet", Property("id", description: "Assigned by the store."));

        pet.Description = "A pet in the store.";

        var written = Component(SpecSchemaWriter.ForRef("#/components/schemas/Pet", new List<SchemaModel> { pet })!, "Pet");

        Assert.Equal("A pet in the store.", written.GetProperty("description").GetString());
        Assert.Equal(
            "Assigned by the store.",
            written.GetProperty("properties").GetProperty("id").GetProperty("description").GetString());
    }

    /// <summary>
    /// A described reference is wrapped, because a <c>$ref</c> takes no siblings.
    /// </summary>
    /// <remarks>
    /// OpenAPI 3.0 ignores every key beside a <c>$ref</c>, so writing the description there would
    /// drop it. <c>allOf</c> is the spelling every tool reads.
    /// </remarks>
    [Fact]
    public void ADescribedReferenceIsWrappedInAllOf() {
        var schemas = new List<SchemaModel> {
            Object("Order", Property("pet", reference: "#/components/schemas/Pet", description: "What was ordered.")),
            Object("Pet", Property("id"))
        };

        var order = Component(SpecSchemaWriter.ForRef("#/components/schemas/Order", schemas)!, "Order");
        var pet = order.GetProperty("properties").GetProperty("pet");

        Assert.Equal("What was ordered.", pet.GetProperty("description").GetString());
        Assert.Equal(
            "#/components/schemas/Pet",
            pet.GetProperty("allOf")[0].GetProperty("$ref").GetString());
    }

    [Fact]
    public void AnUndescribedReferenceIsWrittenBare() {
        var schemas = new List<SchemaModel> {
            Object("Order", Property("pet", reference: "#/components/schemas/Pet")),
            Object("Pet", Property("id"))
        };

        var pet = Component(SpecSchemaWriter.ForRef("#/components/schemas/Order", schemas)!, "Order")
            .GetProperty("properties").GetProperty("pet");

        Assert.Equal("#/components/schemas/Pet", pet.GetProperty("$ref").GetString());
        Assert.False(pet.TryGetProperty("allOf", out _));
    }

    [Fact]
    public void RequiredNamesTheMembersTheContractRequires() {
        var schemas = new List<SchemaModel> {
            Object("Pet", Property("id", required: true), Property("nickname"))
        };

        var required = Component(SpecSchemaWriter.ForRef("#/components/schemas/Pet", schemas)!, "Pet")
            .GetProperty("required");

        Assert.Equal(1, required.GetArrayLength());
        Assert.Equal("id", required[0].GetString());
    }

    /// <summary>
    /// A member bound to a response header is not in the body, so it cannot be required of one.
    /// </summary>
    [Fact]
    public void AHeaderBoundMemberIsNeverRequired() {
        var schemas = new List<SchemaModel> {
            Object("CreatePetOutput",
                Property("pet", required: true),
                Property("location", required: true, headerName: "Location"))
        };

        var written = Component(SpecSchemaWriter.ForRef("#/components/schemas/CreatePetOutput", schemas)!, "CreatePetOutput");
        var required = written.GetProperty("required");

        Assert.Equal(1, required.GetArrayLength());
        Assert.Equal("pet", required[0].GetString());
    }

    [Fact]
    public void AnEnumWritesItsWireValues() {
        var kind = new SchemaModel { Name = "PetKind", Kind = SchemaKind.Enum };

        kind.EnumValues.AddRange(new[] { "cat", "dog" });

        var written = Component(SpecSchemaWriter.ForRef("#/components/schemas/PetKind", new List<SchemaModel> { kind })!, "PetKind");

        Assert.Equal("string", written.GetProperty("type").GetString());
        Assert.Equal("cat", written.GetProperty("enum")[0].GetString());
        Assert.Equal("dog", written.GetProperty("enum")[1].GetString());
    }

    /// <summary>
    /// A type that reaches itself is written once and referenced, rather than expanded forever.
    /// </summary>
    [Fact]
    public void ASelfReferencingSchemaTerminates() {
        var node = Object("Node", Property("child", reference: "#/components/schemas/Node"));

        var written = SpecSchemaWriter.ForRef("#/components/schemas/Node", new List<SchemaModel> { node })!;

        Assert.Single(written.Components);
        Assert.Equal(
            "#/components/schemas/Node",
            Component(written, "Node").GetProperty("properties").GetProperty("child").GetProperty("$ref").GetString());
    }

    [Fact]
    public void TheStatusWordingIsAFallbackForAResponseThatDeclaredNone() {
        Assert.Equal("Created", SpecSchemaWriter.DescriptionFor(null, 201));
        Assert.Equal("Created", SpecSchemaWriter.DescriptionFor("", 201));
        Assert.Equal("Pet created", SpecSchemaWriter.DescriptionFor("Pet created", 201));
    }

    /// <summary>
    /// A map property, as the object it is rather than as the string it fell back to.
    /// </summary>
    /// <remarks>
    /// The writer had no dictionary branch at either level, so a member the model typed
    /// <c>Dictionary&lt;string,int&gt;</c> reached the scalar writer with no type and took its
    /// <c>string</c> default. Refitter generated a string and threw reading the object; Kiota
    /// generated one and read null.
    /// </remarks>
    [Fact]
    public void AMapPropertyIsPublishedAsAnObjectWithAdditionalProperties() {
        var schemas = new List<SchemaModel> {
            Object("Report", new PropertyModel {
                Name = "byStatus", IsDictionary = true, DictionaryValueType = "integer",
                DictionaryValueFormat = "int32"
            })
        };

        var byStatus = Component(SpecSchemaWriter.ForRef("#/components/schemas/Report", schemas)!, "Report")
            .GetProperty("properties").GetProperty("byStatus");

        Assert.Equal("object", byStatus.GetProperty("type").GetString());
        Assert.Equal("integer", byStatus.GetProperty("additionalProperties").GetProperty("type").GetString());
        Assert.Equal("int32", byStatus.GetProperty("additionalProperties").GetProperty("format").GetString());
    }

    /// <summary>A map whose values name a component references it rather than inlining it.</summary>
    [Fact]
    public void AMapOfReferencesReferencesTheValueSchema() {
        var schemas = new List<SchemaModel> {
            Object("Store", new PropertyModel {
                Name = "pets", IsDictionary = true,
                DictionaryValueRef = "#/components/schemas/Pet"
            }),
            Object("Pet", Property("id"))
        };

        var written = SpecSchemaWriter.ForRef("#/components/schemas/Store", schemas)!;

        Assert.Equal(
            "#/components/schemas/Pet",
            Component(written, "Store").GetProperty("properties").GetProperty("pets")
                .GetProperty("additionalProperties").GetProperty("$ref").GetString());

        Assert.Equal("object", Component(written, "Pet").GetProperty("type").GetString());
    }

    /// <summary>
    /// A nullable map keeps both facts. <c>Nullable</c> rewrites the type it finds first, which is
    /// the map's own and not its value's.
    /// </summary>
    [Fact]
    public void ANullableMapIsAnObjectOrNull() {
        var schemas = new List<SchemaModel> {
            Object("Pet", new PropertyModel {
                Name = "tags", IsDictionary = true, DictionaryValueType = "string", IsNullable = true
            })
        };

        var tags = Component(SpecSchemaWriter.ForRef("#/components/schemas/Pet", schemas)!, "Pet")
            .GetProperty("properties").GetProperty("tags");
        var type = tags.GetProperty("type");

        Assert.Equal(2, type.GetArrayLength());
        Assert.Equal("object", type[0].GetString());
        Assert.Equal("null", type[1].GetString());
        Assert.Equal("string", tags.GetProperty("additionalProperties").GetProperty("type").GetString());
    }

    /// <summary>A schema that is itself a map, which the object branch published members-less.</summary>
    [Fact]
    public void ANamedMapSchemaIsPublishedAsAMap() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Counts", Kind = SchemaKind.Dictionary, DictionaryValueType = "integer",
                Description = "How many of each."
            }
        };

        var counts = Component(SpecSchemaWriter.ForRef("#/components/schemas/Counts", schemas)!, "Counts");

        Assert.Equal("object", counts.GetProperty("type").GetString());
        Assert.Equal("How many of each.", counts.GetProperty("description").GetString());
        Assert.Equal("integer", counts.GetProperty("additionalProperties").GetProperty("type").GetString());
    }

    /// <summary>
    /// A schema the contract names for a scalar, which the same missing branch published as an
    /// object.
    /// </summary>
    [Fact]
    public void ANamedPrimitiveSchemaKeepsItsTypeAndFormat() {
        var schemas = new List<SchemaModel> {
            new() { Name = "Sku", Kind = SchemaKind.Primitive, Type = "string", Format = "uuid" }
        };

        var sku = Component(SpecSchemaWriter.ForRef("#/components/schemas/Sku", schemas)!, "Sku");

        Assert.Equal("string", sku.GetProperty("type").GetString());
        Assert.Equal("uuid", sku.GetProperty("format").GetString());
    }
}
