using Xunit;
using Hardened.Idl;

namespace Hardened.OpenApi.SourceGenerator.Tests;

public class NamingHelperTests {
    [Theory]
    [InlineData("petStore", "PetStore")]
    [InlineData("pet_store", "PetStore")]
    [InlineData("pet-store", "PetStore")]
    [InlineData("Pet", "Pet")]
    [InlineData("", "")]
    public void ToPascalCase(string input, string expected) {
        Assert.Equal(expected, NamingHelper.ToPascalCase(input));
    }

    [Theory]
    [InlineData("PetStore", "petStore")]
    [InlineData("Pet", "pet")]
    public void ToCamelCase(string input, string expected) {
        Assert.Equal(expected, NamingHelper.ToCamelCase(input));
    }

    [Theory]
    [InlineData("class", "@class")]
    [InlineData("name", "name")]
    [InlineData("string", "@string")]
    public void EscapeIdentifier(string input, string expected) {
        Assert.Equal(expected, NamingHelper.EscapeIdentifier(input));
    }

    [Theory]
    [InlineData("Pet", "IPetService")]
    [InlineData("Recipe", "IRecipeService")]
    public void ToInterfaceName(string tag, string expected) {
        Assert.Equal(expected, NamingHelper.ToInterfaceName(tag));
    }

    [Theory]
    [InlineData("Pet", "PetController")]
    [InlineData("Recipe", "RecipeController")]
    public void ToControllerName(string tag, string expected) {
        Assert.Equal(expected, NamingHelper.ToControllerName(tag));
    }
}
