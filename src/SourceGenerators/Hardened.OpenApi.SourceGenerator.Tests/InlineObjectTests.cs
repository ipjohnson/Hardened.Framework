using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Objects written inline, which used to be discarded before any emitter saw them.
/// </summary>
/// <remarks>
/// The property mapped to <c>JsonElement</c> and the nested shape went with it — including every
/// constraint declared on the nested properties, which then could not be enforced.
/// </remarks>
public class InlineObjectTests {

    private static OpenApiSpecModel Parse() {
        var model = OpenApiSpecParser.Parse(Specs.InlineObjects, "test", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    [Fact]
    public void AnInlineObjectBecomesASchemaNamedForWhereItSits() {
        var names = Parse().Schemas.Select(s => s.Name).ToList();

        Assert.Contains("PetAddress", names);
    }

    /// <summary>Nesting goes all the way down, not one level.</summary>
    [Fact]
    public void NestedInlineObjectsAreLiftedToo() {
        Assert.Contains("PetAddressGeo", Parse().Schemas.Select(s => s.Name));
    }

    [Fact]
    public void ThePropertyReferencesTheLiftedSchemaRatherThanFallingBackToJsonElement() {
        var address = Parse().Schemas.First(s => s.Name == "Pet")
            .Properties.First(p => p.Name == "address");

        Assert.Equal("#/components/schemas/PetAddress", address.Ref);
        Assert.Equal("PetAddress", TypeMapper.MapPropertyToCSharpType(address));
    }

    /// <summary>The lifted schema keeps everything the inline one declared.</summary>
    [Fact]
    public void TheLiftedSchemaKeepsItsPropertiesAndConstraints() {
        var address = Parse().Schemas.First(s => s.Name == "PetAddress");

        Assert.Equal("Where the pet lives.", address.Description);
        Assert.Contains("city", address.Required);

        var city = address.Properties.First(p => p.Name == "city");

        Assert.Equal(2, city.MinLength);
    }

    [Fact]
    public void TheGeneratedCodeCompilesAndIsTyped() {
        var generated = OpenApiGenerator.Run(Specs.InlineObjects).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("public partial record PetAddress(", generated);
        Assert.Contains("public partial record PetAddressGeo(", generated);
        Assert.Contains("PetAddress Address", generated);

        foreach (var line in generated.Split('\n')) {
            if (line.TrimStart().StartsWith("public partial record ")) {
                Assert.DoesNotContain("JsonElement", line);
            }
        }
    }

    /// <summary>
    /// A handler using the lifted types, which is the thing that could not be written before.
    /// </summary>
    [Fact]
    public void AHandlerCanUseTheLiftedTypes() {
        OpenApiGenerator.Run(
                Specs.InlineObjects,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class PetServiceImpl : IPetService {
                        public Task<Pet> ListPets() =>
                            Task.FromResult(new Pet("1", new PetAddress("Boston")));
                    }
                    """))
            .AssertNoErrors();
    }
}
