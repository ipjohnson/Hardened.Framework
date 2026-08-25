using Hardened.Idl.Emitters;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

public class SchemaRecordEmitterTests {
    [Fact]
    public void Emit_SimpleRecord_GeneratesCorrectOutput() {
        var schema = new SchemaModel {
            Name = "Pet",
            Kind = SchemaKind.Object,
            Required = new List<string> { "id", "name" },
            Properties = new List<PropertyModel> {
                new() { Name = "id", Type = "string", IsRequired = true },
                new() { Name = "name", Type = "string", IsRequired = true },
                new() { Name = "tag", Type = "string", IsRequired = false }
            }
        };

        var result = EmitterHarness.Schema(schema);

        Assert.Contains("namespace Test.Api.Models\n{", result);
        Assert.Contains("public partial record Pet(", result);
        Assert.Contains("string Id,", result);
        Assert.Contains("string Name,", result);
        Assert.Contains("string? Tag = default)", result);
    }

    [Fact]
    public void Emit_EmptyRecord_GeneratesEmptyRecord() {
        var schema = new SchemaModel {
            Name = "EmptyModel",
            Kind = SchemaKind.Object,
            Properties = new List<PropertyModel>()
        };

        var result = EmitterHarness.Schema(schema);

        Assert.Contains("public partial record EmptyModel;", result);
    }

    [Fact]
    public void Emit_WithArrayProperty_GeneratesList() {
        var schema = new SchemaModel {
            Name = "PetList",
            Kind = SchemaKind.Object,
            Required = new List<string> { "items" },
            Properties = new List<PropertyModel> {
                new() {
                    Name = "items",
                    IsArray = true,
                    ArrayItemsRef = "#/components/schemas/Pet",
                    IsRequired = true
                }
            }
        };

        var result = EmitterHarness.Schema(schema);

        Assert.Contains("List<Pet> Items)", result);
    }
}
