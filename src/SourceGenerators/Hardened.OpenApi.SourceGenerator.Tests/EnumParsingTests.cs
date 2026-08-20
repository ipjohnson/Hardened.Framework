using Hardened.Idl.Models;
using Hardened.Idl;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What the parser keeps of an <c>enum</c>, which decides what every emitter after it can say.
/// </summary>
/// <remarks>
/// The member reader took strings alone. <c>ParseSchemaKind</c> still recognised
/// <c>type: integer, enum: [1, 2, 3]</c> as an enum, so it arrived with every member filtered away
/// and emitted an empty C# enum whose converter threw on every value - on a clean, silent build.
/// </remarks>
public class EnumParsingTests {

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static SchemaModel Enum(string name, string members) =>
        Parse(
            $$"""
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /pets:
                get:
                  tags: [Pet]
                  operationId: listPets
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/{{name}}'
            components:
              schemas:
            {{members}}
            """).Schemas.Single(schema => schema.Name == name);

    [Fact]
    public void AStringEnumKeepsItsValuesAndItsType() {
        var schema = Enum("PetStatus",
            """
                PetStatus:
                  type: string
                  enum: [available, pending, sold]
            """);

        Assert.Equal(SchemaKind.Enum, schema.Kind);
        Assert.Equal("string", schema.Type);
        Assert.Equal(new[] { "available", "pending", "sold" }, schema.EnumValues);
    }

    [Fact]
    public void AnIntegerEnumKeepsItsValuesAndItsType() {
        var schema = Enum("PetSize",
            """
                PetSize:
                  type: integer
                  enum: [1, 5, 25]
            """);

        Assert.Equal(SchemaKind.Enum, schema.Kind);
        Assert.Equal("integer", schema.Type);
        Assert.Equal(new[] { "1", "5", "25" }, schema.EnumValues);
    }

    /// <summary>
    /// The type is read from the members, because a document is not obliged to write
    /// <c>type:</c> beside its <c>enum:</c> and plenty do not.
    /// </summary>
    [Fact]
    public void AnEnumWithNoDeclaredTypeIsReadFromItsMembers() {
        var schema = Enum("PetSize",
            """
                PetSize:
                  enum: [1, 5, 25]
            """);

        Assert.Equal("integer", schema.Type);
        Assert.Equal(new[] { "1", "5", "25" }, schema.EnumValues);
    }

    [Fact]
    public void XEnumVarnamesNamesTheMembers() {
        var schema = Enum("PetSize",
            """
                PetSize:
                  type: integer
                  enum: [1, 5, 25]
                  x-enum-varnames: [Small, Medium, Large]
            """);

        Assert.True(schema.EnumMemberNamesAreDeclared);
        Assert.Equal(new[] { "Small", "Medium", "Large" }, schema.EnumMemberNames);
    }

    /// <summary>NSwag's spelling of the same thing.</summary>
    [Fact]
    public void XEnumNamesNamesTheMembersToo() {
        var schema = Enum("PetSize",
            """
                PetSize:
                  type: integer
                  enum: [1, 5]
                  x-enumNames: [Small, Large]
            """);

        Assert.True(schema.EnumMemberNamesAreDeclared);
        Assert.Equal(new[] { "Small", "Large" }, schema.EnumMemberNames);
    }

    /// <summary>
    /// A name list that does not line up with the values is ignored rather than half-applied.
    /// </summary>
    [Fact]
    public void AMismatchedNameListIsIgnored() {
        var schema = Enum("PetSize",
            """
                PetSize:
                  type: integer
                  enum: [1, 5, 25]
                  x-enum-varnames: [Small, Large]
            """);

        Assert.False(schema.EnumMemberNamesAreDeclared);
    }

    /// <summary>
    /// An enum with no names declared says so, which is what keeps Smithy's own member names -
    /// a parser hint rather than a request - out of the allocator's way.
    /// </summary>
    [Fact]
    public void AnEnumWithNoNamesDeclaredSaysSo() {
        var schema = Enum("PetStatus",
            """
                PetStatus:
                  type: string
                  enum: [available, sold]
            """);

        Assert.False(schema.EnumMemberNamesAreDeclared);
    }

    /// <summary>
    /// Both kinds at once is marked for the diagnostic pass, which reports it as a build error.
    /// </summary>
    /// <remarks>
    /// Honouring the strings puts the numbers out of reach and honouring the numbers puts the
    /// strings out of reach, so either choice silently drops half of what a caller may send.
    /// </remarks>
    [Fact]
    public void AMixedEnumIsMarkedRatherThanResolved() {
        var schema = Enum("Muddle",
            """
                Muddle:
                  enum: [available, 1]
            """);

        Assert.Equal("mixed-enum", schema.Type);
    }

    /// <summary>
    /// A boolean enum is not one. Two values is a <c>bool</c>, and Stripe's
    /// <c>deleted: {type: boolean, enum: [true]}</c> is a constant rather than a type.
    /// </summary>
    [Fact]
    public void ABooleanEnumContributesNoMembers() {
        var schema = Enum("Deleted",
            """
                Deleted:
                  type: boolean
                  enum: [true]
            """);

        Assert.Empty(schema.EnumValues);
    }
}
