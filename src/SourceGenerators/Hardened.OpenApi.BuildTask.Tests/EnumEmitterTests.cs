using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.OpenApi.SourceGenerator.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

public class EnumEmitterTests {
    [Fact]
    public void Emit_GeneratesEnumWithJsonConverter() {
        var schema = new SchemaModel {
            Name = "PetStatus",
            Kind = SchemaKind.Enum,
            EnumValues = new List<string> { "available", "pending", "sold" }
        };

        var result = EnumEmitter.Emit(schema, "Test.Api");

        Assert.Contains("namespace Test.Api.Models\n{", result);
        Assert.Contains("[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]", result);
        Assert.Contains("public enum PetStatus", result);
        Assert.Contains("Available,", result);
        Assert.Contains("Pending,", result);
        Assert.Contains("Sold", result);
    }
}
