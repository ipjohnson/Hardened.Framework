using Hardened.Idl.Emitters;
using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

public class SchemaEnumEmitterTests {

    /// <summary>
    /// The enum names the converter emitted beside it, not <c>JsonStringEnumConverter</c>.
    /// </summary>
    /// <remarks>
    /// This schema is the whole argument. Its values are <c>available</c>, <c>pending</c> and
    /// <c>sold</c>; its members are <c>Available</c>, <c>Pending</c> and <c>Sold</c>.
    /// <c>JsonStringEnumConverter</c> writes the member name, so every one of those left as the
    /// wrong string - and an attribute on the type wins over the converter the resolver supplies,
    /// so the generated one that knows the values was never consulted. Naming it here is what makes
    /// the two agree.
    /// </remarks>
    [Fact]
    public void Emit_GeneratesEnumNamingItsOwnConverter() {
        var schema = new SchemaModel {
            Name = "PetStatus",
            Kind = SchemaKind.Enum,
            EnumValues = new List<string> { "available", "pending", "sold" }
        };

        var result = EmitterHarness.Schema(schema);

        Assert.Contains("namespace Test.Api.Models\n{", result);
        Assert.Contains("[JsonConverter(typeof(global::Test.Api.Models.PetStatusConverter))]", result);
        Assert.DoesNotContain("JsonStringEnumConverter", result);
        Assert.Contains("public enum PetStatus", result);
        Assert.Contains("Available,", result);
        Assert.Contains("Pending,", result);
        Assert.Contains("Sold", result);
    }
}
