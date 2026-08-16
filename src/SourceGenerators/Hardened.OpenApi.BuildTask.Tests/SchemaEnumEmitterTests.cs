using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

public class SchemaEnumEmitterTests {
    [Fact]
    public void Emit_GeneratesEnumWithJsonConverter() {
        var schema = new SchemaModel {
            Name = "PetStatus",
            Kind = SchemaKind.Enum,
            EnumValues = new List<string> { "available", "pending", "sold" }
        };

        var result = EmitterHarness.Schema(schema);

        Assert.Contains("namespace Test.Api.Models\n{", result);
        Assert.Contains("[JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]", result);
        Assert.Contains("public enum PetStatus", result);
        Assert.Contains("Available,", result);
        Assert.Contains("Pending,", result);
        Assert.Contains("Sold", result);
    }
}
