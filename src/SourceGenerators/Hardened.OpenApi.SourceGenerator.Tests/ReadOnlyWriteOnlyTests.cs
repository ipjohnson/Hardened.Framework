using Hardened.Idl.Models;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <c>readOnly</c> and <c>writeOnly</c> as directions on one type.
/// </summary>
/// <remarks>
/// <para>
/// A schema using either keyword describes two shapes - a server-populated <c>id</c> must not be
/// sent in a create request, and a <c>secret</c> must not come back in a response - but it is
/// emitted as a single record. Two types would be the literal reading; it would also mean every
/// handler converting between <c>PetRequest</c> and <c>PetResponse</c> by hand.
/// </para>
/// <para>
/// One record works because the two directions are enforced separately from the C# shape. A
/// <c>readOnly</c> property leaves the constructor and becomes <c>{ get; init; }</c>, so
/// deserialization cannot reach it while <c>body with { Id = NewId() }</c> still can - <c>with</c>
/// uses the init accessor, which the JSON resolver knows nothing about. A <c>writeOnly</c> property
/// stays positional and loses its getter, so it deserializes and never serializes.
/// </para>
/// </remarks>
public class ReadOnlyWriteOnlyTests {

    private static PropertyModel Property(string schema, string name) {
        var model = OpenApiSpecParser.Parse(Specs.ReadOnlyAndWriteOnly, "test", CancellationToken.None);

        Assert.NotNull(model);

        return model!.Schemas.First(s => s.Name == schema).Properties.First(p => p.Name == name);
    }

    private static string Generated(string spec) =>
        OpenApiGenerator.Run(spec).AssertNoErrors().SourceContaining("petstore.g.cs");

    /// <summary>
    /// The text between two markers, so an assertion applies to one block of the resolver rather
    /// than to the whole file. <c>PropertyName = "id"</c> contains <c>Name = "id"</c>, so a
    /// file-wide <c>DoesNotContain</c> for a parameter name matches the property list instead.
    /// </summary>
    private static string Between(string source, string start, string end) {
        var from = source.IndexOf(start, StringComparison.Ordinal);

        Assert.True(from >= 0, $"'{start}' is not in the generated source.");

        var rest = source[(from + start.Length)..];
        var to = rest.IndexOf(end, StringComparison.Ordinal);

        Assert.True(to >= 0, $"'{end}' does not follow '{start}'.");

        return rest[..to];
    }

    [Fact]
    public void TheKeywordsAreRead() {
        Assert.True(Property("Pet", "id").IsReadOnly);
        Assert.False(Property("Pet", "id").IsWriteOnly);

        Assert.True(Property("Pet", "secret").IsWriteOnly);
        Assert.False(Property("Pet", "secret").IsReadOnly);

        Assert.False(Property("Pet", "name").IsReadOnly);
        Assert.False(Property("Pet", "name").IsWriteOnly);
    }

    /// <summary>
    /// The property that would otherwise be a required constructor parameter is declared in the
    /// record body instead, initialized so the non-nullable type holds.
    /// </summary>
    [Fact]
    public void AReadOnlyPropertyIsAnInitOnlyMember() {
        var generated = Generated(Specs.ReadOnlyAndWriteOnly);

        Assert.Contains("public string Id { get; init; } = default!;", generated);

        var record = generated.Split('\n').First(line => line.Contains("record Pet("));

        Assert.DoesNotContain("Id", record);
    }

    /// <summary>
    /// A <c>writeOnly</c> property is an ordinary parameter - the direction is enforced by the
    /// resolver, not by the C# shape.
    /// </summary>
    [Fact]
    public void AWriteOnlyPropertyStaysPositional() {
        var record = Generated(Specs.ReadOnlyAndWriteOnly)
            .Split('\n').First(line => line.Contains("record Pet("));

        Assert.Contains("Secret", record);
    }

    /// <summary>
    /// The half that actually enforces it. A null accessor makes System.Text.Json skip the property
    /// in that direction, so these two lines are the feature - the record shape only decides which
    /// of them is reachable.
    /// </summary>
    [Fact]
    public void TheResolverDropsEachPropertyInOneDirection() {
        var generated = Generated(Specs.ReadOnlyAndWriteOnly);

        // Serialized: a response carries the server-assigned id.
        Assert.Contains("PropertyName = \"id\"", generated);
        Assert.Contains("Getter = static obj => ((Pet)obj).Id,", generated);

        // Never serialized: the secret does not come back.
        Assert.Contains("Getter = null,", Between(generated, "PropertyName = \"secret\"", "}),"));
    }

    /// <summary>
    /// The constructor metadata and the constructor have to describe the same parameter list. The
    /// resolver casts a positional argument array by index, so a read-only property left in one list
    /// and not the other puts every later argument in the wrong parameter - and does it at run time,
    /// where a build catches nothing.
    /// </summary>
    [Fact]
    public void TheConstructorMetadataMatchesTheConstructor() {
        var generated = Generated(Specs.ReadOnlyAndWriteOnly);

        var creator = Between(
            generated, "ObjectWithParameterizedConstructorCreator = static args => new Pet(", "),");

        Assert.DoesNotContain("Id", creator);

        // To the close of CreateObjectInfo, which the parameter list is the last member of. Its own
        // entries each end in "}," so that cannot be the terminator.
        var parameters = Between(
            generated,
            "ConstructorParameterMetadataInitializer = static () => new JsonParameterInfoValues[]",
            "});");

        Assert.DoesNotContain("Name = \"id\",", parameters);

        // The parameters it does describe are the ones the constructor takes, in order.
        Assert.Contains("Name = \"petType\",", parameters);
        Assert.Contains("Name = \"name\",", parameters);
        Assert.Contains("Name = \"secret\",", parameters);
    }

    /// <summary>
    /// A read-only property is not validated. Requiredness in OpenAPI is scoped to a direction, and
    /// <c>required</c> + <c>readOnly</c> means "always present in a response" - enforcing it on the
    /// request would reject the create call of a client that correctly omitted the value.
    /// </summary>
    [Fact]
    public void AReadOnlyPropertyCarriesNoConstraints() {
        Assert.False(Property("Pet", "id").Constrained);

        var generated = Generated(Specs.ReadOnlyAndWriteOnly);

        var member = generated.Split('\n').First(line => line.Contains("public string Id"));

        Assert.DoesNotContain("Required", member);
        Assert.DoesNotContain("StringLength", member);

        // The constrained sibling still is, so this is not just an empty spec.
        Assert.Contains("StringLength", generated);
    }

    /// <summary>
    /// <c>allOf</c> merges a base's properties into the derived schema, so both would declare the
    /// read-only one. A positional parameter is fine that way - the derived record forwards it - but
    /// a redeclared member hides the inherited one, which is CS0108 and an error under
    /// <c>ContinuousIntegrationBuild</c>. The derived record inherits it instead.
    /// </summary>
    [Fact]
    public void ADerivedRecordDoesNotRedeclareTheBaseMember() {
        var generated = Generated(Specs.ReadOnlyAndWriteOnly);

        Assert.Single(
            generated.Split('\n'), line => line.Contains("public string Id { get; init; }"));

        // And it still forwards the base's real constructor parameters.
        Assert.Contains("record Dog(", generated);
    }

    /// <summary>
    /// A schema whose every property is read-only has no constructor parameters at all, so the
    /// resolver needs the parameterless creator rather than an empty positional one.
    /// </summary>
    [Fact]
    public void ASchemaOfOnlyReadOnlyPropertiesGetsAParameterlessCreator() {
        var generated = Generated(Specs.ReadOnlyOnly);

        Assert.Contains("ObjectCreator = static () => new Receipt(),", generated);
        Assert.DoesNotContain("ObjectWithParameterizedConstructorCreator = static args => new Receipt(", generated);

        // Still serialized - the property exists, it just cannot be sent.
        Assert.Contains("Getter = static obj => ((Receipt)obj).Id,", generated);
    }
}
