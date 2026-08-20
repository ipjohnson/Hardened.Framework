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

    /// <summary>
    /// An integer enum carries its declared numbers, and its members carry them too.
    /// </summary>
    /// <remarks>
    /// This produced an empty C# enum. <c>ParseSchemaKind</c> recognised <c>enum:</c> whatever its
    /// members were and the member reader dropped everything that was not a string, so the type had
    /// no members at all and the converter beside it was a switch whose only arm threw - on a build
    /// that was clean and silent.
    /// </remarks>
    [Fact]
    public void Emit_GeneratesAnIntegerEnumWithItsDeclaredValues() {
        var result = EmitterHarness.Schema(new SchemaModel {
            Name = "PetSize",
            Kind = SchemaKind.Enum,
            Type = "integer",
            EnumValues = new List<string> { "1", "5", "25" }
        });

        Assert.Contains("public enum PetSize", result);
        Assert.Contains("Value1 = 1", result);
        Assert.Contains("Value5 = 5", result);
        Assert.Contains("Value25 = 25", result);
    }

    /// <summary>
    /// <c>x-enum-varnames</c> names the members, which is what an integer enum most needs.
    /// </summary>
    [Fact]
    public void Emit_UsesTheNamesTheDocumentDeclaredForAnIntegerEnum() {
        var result = EmitterHarness.Schema(new SchemaModel {
            Name = "PetSize",
            Kind = SchemaKind.Enum,
            Type = "integer",
            EnumValues = new List<string> { "1", "5", "25" },
            EnumMemberNames = new List<string> { "Small", "Medium", "Large" },
            EnumMemberNamesAreDeclared = true
        });

        Assert.Contains("Small = 1", result);
        Assert.Contains("Medium = 5", result);
        Assert.Contains("Large = 25", result);
    }

    /// <summary>
    /// A string enum stays exactly as it was - no explicit values, no underlying type.
    /// </summary>
    /// <remarks>
    /// The half of the integer-enum change that could have gone wrong quietly: numbering a string
    /// enum's members would put a meaningless integer into the public surface of every existing
    /// generated type.
    /// </remarks>
    [Fact]
    public void Emit_LeavesAStringEnumUnnumbered() {
        var result = EmitterHarness.Schema(new SchemaModel {
            Name = "PetStatus",
            Kind = SchemaKind.Enum,
            Type = "string",
            EnumValues = new List<string> { "available", "pending" }
        });

        Assert.Contains("Available,", result);

        // With a space after the '=', so the converter's own "Available => ..." arm does not match.
        Assert.DoesNotContain("Available = ", result);
        Assert.DoesNotContain("enum PetStatus :", result);
    }

    /// <summary>
    /// A value outside <c>int</c> widens the underlying type rather than failing to compile.
    /// </summary>
    [Fact]
    public void Emit_WidensAnIntegerEnumThatDoesNotFitInInt() {
        var result = EmitterHarness.Schema(new SchemaModel {
            Name = "Big",
            Kind = SchemaKind.Enum,
            Type = "integer",
            EnumValues = new List<string> { "1", "9999999999" }
        });

        Assert.Contains("Value9999999999 = 9999999999", result);
        Assert.Contains("Int64", result);
    }
}
