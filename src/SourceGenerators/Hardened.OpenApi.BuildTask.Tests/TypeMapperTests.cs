using Hardened.OpenApi.SourceGenerator;
using Xunit;
using Hardened.Idl;

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
    [InlineData("number", null, "double")]
    [InlineData("boolean", null, "bool")]
    public void MapToCSharpType_BasicTypes(string type, string? format, string expected) {
        var result = TypeMapper.MapToCSharpType(type, format);
        Assert.Equal(expected, result);
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
