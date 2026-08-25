using System.Collections.Generic;
using System.Text.Json;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Xunit;

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
        string? description = null, bool required = false, string? headerName = null) =>
        new() {
            Name = name, Type = reference == null ? type : null, Ref = reference,
            Description = description, IsRequired = required, HeaderName = headerName
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
}
