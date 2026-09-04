using Hardened.OpenApi.SourceGenerator;
using Xunit;
using Hardened.Idl;
using Hardened.Generation;
using Hardened.Generation.Models;

namespace Hardened.OpenApi.BuildTask.Tests;

public class TypeMapperTests {
    [Theory]
    [InlineData("string", null, "string")]
    [InlineData("string", "date-time", "DateTimeOffset")]
    [InlineData("string", "date", "DateOnly")]
    [InlineData("string", "uuid", "string")]
    [InlineData("string", "byte", "byte[]")]
    [InlineData("string", "binary", "byte[]")]
    [InlineData("integer", null, "int")]
    [InlineData("integer", "int32", "int")]
    [InlineData("integer", "int64", "long")]
    [InlineData("number", "float", "float")]
    [InlineData("number", "double", "double")]
    [InlineData("number", "decimal", "decimal")]
    [InlineData("string", "number", "decimal")]
    [InlineData("number", null, "double")]
    [InlineData("boolean", null, "bool")]
    public void MapToCSharpType_BasicTypes(string type, string? format, string expected) {
        var result = TypeMapper.MapToCSharpType(type, format);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// H-20 from the 0.19.0-rc1000 trial: the value of a map and the item of an array parameter
    /// were mapped with a literal <c>null</c> format, so the one pair that needs the format to be
    /// exact - money - was binary floating point in both places and exact everywhere else.
    /// </summary>
    [Fact]
    public void MapPropertyToCSharpType_KeepsTheFormatOfAMapValue() {
        var property = new PropertyModel {
            Name = "quotes", IsDictionary = true, DictionaryValueType = "number", DictionaryValueFormat = "decimal",
        };

        Assert.Equal("Dictionary<string, decimal>", TypeMapper.MapPropertyToCSharpType(property));
    }

    [Fact]
    public void MapParameterToCSharpType_KeepsTheFormatOfAnArrayItem() {
        var parameter = new ParameterModel {
            Name = "amounts", IsArray = true, ArrayItemsType = "number", ArrayItemsFormat = "decimal",
        };

        Assert.Equal("List<decimal>", TypeMapper.MapParameterToCSharpType(parameter));
    }

    [Fact]
    public void MapToCSharpType_WithRef_ReturnsTypeName() {
        var result = TypeMapper.MapToCSharpType(null, null, "#/components/schemas/Pet");
        Assert.Equal("Pet", result);
    }

    [Fact]
    public void GetRefName_ExtractsLastSegment() {
        Assert.Equal("Pet", TypeMapper.GetRefName("#/components/schemas/Pet"));
        Assert.Equal("CreatePetRequest", TypeMapper.GetRefName("#/components/schemas/CreatePetRequest"));
    }
}
