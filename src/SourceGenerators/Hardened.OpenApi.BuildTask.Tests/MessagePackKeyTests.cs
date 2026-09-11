using System.Linq;
using System.Threading;
using Hardened.Idl;
using Hardened.Idl.Emitters;
using Hardened.Idl.Validation;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The field identity a MessagePack wire carries, from the contract through to the generated model.
/// </summary>
/// <remarks>
/// <para>
/// The key is the whole point of a keyed format: a client generated last month and a server built
/// today agree about which member is which without agreeing about names. So the index has to come
/// from the contract and reach the model unchanged, and a member the contract does not key has to
/// stop the build rather than be given one - see <c>SpecDiagnostics</c>.
/// </para>
/// </remarks>
public class MessagePackKeyTests {

    private const string Keyed = """
        openapi: 3.0.0
        info: { title: Depot, version: '1.0' }
        paths:
          /readings:
            get:
              operationId: listReadings
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Reading' }
        components:
          schemas:
            Reading:
              type: object
              required: [sensor]
              properties:
                sensor:
                  type: string
                  x-message-pack-index: 0
                value:
                  type: integer
                  x-message-pack-index: 7
                note:
                  type: string
        """;

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "depot", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static PropertyModel Property(ServiceSpecModel model, string name) =>
        model.Schemas.Single(schema => schema.Name == "Reading").Properties
            .Single(property => property.Name == name);

    [Fact]
    public void TheIndexTheContractStatesReachesTheModel() {
        var model = Parse(Keyed);

        Assert.Equal(0, Property(model, "sensor").MessagePackIndex);
        Assert.Equal(7, Property(model, "value").MessagePackIndex);
    }

    /// <summary>
    /// Absent means absent. Nothing here fills the gap in, because an index this build chose would
    /// move the next time a property was added above it.
    /// </summary>
    [Fact]
    public void APropertyWithNoIndexGetsNone() {
        Assert.Null(Property(Parse(Keyed), "note").MessagePackIndex);
    }

    /// <summary>
    /// MessagePack rejects a negative key outright, so reading one as absent sends the author to
    /// the property that has to change rather than into a generated file.
    /// </summary>
    [Fact]
    public void ANegativeIndexIsReadAsAbsent() {
        var model = Parse(Keyed.Replace("x-message-pack-index: 7", "x-message-pack-index: -1"));

        Assert.Null(Property(model, "value").MessagePackIndex);
    }

    /// <summary>
    /// The index is a fact about the contract and not about the mode a project builds in, so it is
    /// read whatever <c>$(HardenedSerializer)</c> says. A contract carrying indices stays valid for
    /// a project that is not using them.
    /// </summary>
    [Fact]
    public void TheIndexIsReadWithoutTheKeyedMode() {
        Assert.Equal(SpecSerializer.Json, Parse(Keyed).Serializer);
        Assert.Equal(0, Property(Parse(Keyed), "sensor").MessagePackIndex);
    }

    private static string Emit(SpecSerializer serializer, params PropertyModel[] properties) {
        var schema = new SchemaModel { Name = "Reading", Kind = SchemaKind.Object };

        foreach (var property in properties) {
            schema.Properties.Add(property);
        }

        var patterns = new PatternRegistry(EmitterHarness.RootNamespace + ".Validation", "spec");

        return EmitterHarness.Write(ns =>
            SchemaEmitter.Emit(
                ns, schema, EmitterHarness.ModelsNamespace, patterns, null, null, serializer));
    }

    private static PropertyModel At(string name, int? index) =>
        new() { Name = name, Type = "string", IsRequired = true, MessagePackIndex = index };

    [Fact]
    public void TheKeyedModeEmitsTheObjectAttributeAndEveryKey() {
        var emitted = Emit(SpecSerializer.MessagePackKeyed, At("sensor", 0), At("value", 7));

        Assert.Contains("MessagePackObject]", emitted);
        Assert.Contains("Key(0)]", emitted);
        Assert.Contains("Key(7)]", emitted);
    }

    /// <summary>
    /// The attribute has to reach the property rather than the parameter. A positional record's
    /// parameter and the property it declares are one syntactic position, and MessagePack reads
    /// properties.
    /// </summary>
    [Fact]
    public void TheKeyTargetsTheProperty() {
        Assert.Contains("[property: ", Emit(SpecSerializer.MessagePackKeyed, At("sensor", 0)));
        Assert.Contains("Key(0)]", Emit(SpecSerializer.MessagePackKeyed, At("sensor", 0)));
    }

    /// <summary>
    /// keyAsPropertyName, with the name the document publishes pinned on each member.
    /// </summary>
    /// <remarks>
    /// The attribute alone would write the C# member's name, and that name is a PascalCasing of the
    /// wire name - so <c>unit_price</c> would leave as <c>UnitPrice</c> and agree with a generated
    /// client only for as long as two unrelated PascalCase implementations agree. The index is
    /// ignored here whatever the contract states: under this mode the name is the identity.
    /// </remarks>
    [Fact]
    public void TheNamedModeKeysByTheWireName() {
        var emitted = Emit(SpecSerializer.MessagePackNamed, At("unit_price", 0), At("value", 7));

        Assert.Contains("MessagePackObject(true)]", emitted);
        Assert.Contains("Key(\"unit_price\")]", emitted);
        Assert.DoesNotContain("Key(0)]", emitted);
    }

    /// <summary>
    /// A project that never asked for MessagePack gets no MessagePack attribute, whatever the
    /// contract states. The package is not referenced, so an attribute here would not compile.
    /// </summary>
    [Fact]
    public void TheJsonModeEmitsNeither() {
        var emitted = Emit(SpecSerializer.Json, At("sensor", 0));

        Assert.DoesNotContain("MessagePack", emitted);
    }

    /// <summary>
    /// A member bound to a response header is in neither payload, so it is excluded rather than
    /// keyed - the same answer <c>[JsonIgnore]</c> gives it on the JSON side. Left unannotated it
    /// would be a member of a keyed object with no key, which MessagePack refuses.
    /// </summary>
    [Fact]
    public void AHeaderBoundMemberIsIgnoredRatherThanKeyed() {
        var emitted = Emit(
            SpecSerializer.MessagePackKeyed,
            At("sensor", 0),
            new PropertyModel {
                Name = "etag", Type = "string", IsRequired = true, HeaderName = "ETag"
            });

        Assert.Contains("IgnoreMember]", emitted);
    }

    [Fact]
    public void AHeaderBoundMemberIsIgnoredUnderTheNamedModeToo() {
        var emitted = Emit(
            SpecSerializer.MessagePackNamed,
            new PropertyModel {
                Name = "etag", Type = "string", IsRequired = true, HeaderName = "ETag"
            });

        Assert.Contains("IgnoreMember]", emitted);
    }

    private static ServiceSpecModel Spec(SpecSerializer serializer, params PropertyModel[] properties) {
        var schema = new SchemaModel { Name = "Reading", Kind = SchemaKind.Object };

        foreach (var property in properties) {
            schema.Properties.Add(property);
        }

        return new ServiceSpecModel {
            FileName = "spec", Serializer = serializer, Schemas = { schema }
        };
    }

    private static SpecDiagnostics.Problem[] Findings(ServiceSpecModel model) =>
        SpecDiagnostics.Find(model, "HOAT").Where(problem => problem.Code == "HOAT033").ToArray();

    /// <summary>
    /// The whole of the "explicit only" decision, in one assertion. Declaration order and a name
    /// hash both assign an index that can move or collide, and a wire format whose keys move is
    /// worse than one that refuses to build.
    /// </summary>
    [Fact]
    public void AnUnkeyedPropertyStopsTheBuild() {
        var problem = Assert.Single(
            Findings(Spec(SpecSerializer.MessagePackKeyed, At("sensor", 0), At("note", null))));

        Assert.True(problem.Fatal);
        Assert.Contains("note", problem.Message);
    }

    /// <summary>
    /// Naming the next free index rather than only the problem: the author has to choose a number,
    /// and every number already in the schema is one they must not choose.
    /// </summary>
    [Fact]
    public void TheFindingNamesTheNextFreeIndex() {
        var problem = Assert.Single(
            Findings(Spec(SpecSerializer.MessagePackKeyed, At("sensor", 0), At("value", 7),
                At("note", null))));

        Assert.Contains("x-message-pack-index: 8", problem.Message);
    }

    /// <summary>
    /// Two unkeyed properties get two different suggestions, so applying both leaves a schema that
    /// builds rather than one that collides.
    /// </summary>
    [Fact]
    public void TwoUnkeyedPropertiesAreSuggestedDifferentIndices() {
        var problems = Findings(
            Spec(SpecSerializer.MessagePackKeyed, At("sensor", 3), At("note", null),
                At("unit", null)));

        Assert.Equal(2, problems.Length);
        Assert.Contains("x-message-pack-index: 4", problems[0].Message);
        Assert.Contains("x-message-pack-index: 5", problems[1].Message);
    }

    /// <summary>
    /// One index identifies one member. MessagePack refuses the pair when it writes the formatter,
    /// which reports against generated code rather than against the contract that caused it.
    /// </summary>
    [Fact]
    public void TwoPropertiesAtOneIndexAreReported() {
        var problem = Assert.Single(
            Findings(Spec(SpecSerializer.MessagePackKeyed, At("sensor", 2), At("value", 2))));

        Assert.Contains("sensor", problem.Message);
        Assert.Contains("value", problem.Message);
    }

    /// <summary>
    /// A header-bound member is in neither payload, so there is nothing to key and nothing to
    /// report - it is excluded from the object rather than given an index.
    /// </summary>
    [Fact]
    public void AHeaderBoundMemberNeedsNoIndex() {
        Assert.Empty(Findings(Spec(
            SpecSerializer.MessagePackKeyed,
            At("sensor", 0),
            new PropertyModel { Name = "etag", Type = "string", HeaderName = "ETag" })));
    }

    /// <summary>
    /// Nothing is demanded of a project that is not keying anything. A contract with no indices is
    /// the ordinary case, and the named and JSON modes never read one.
    /// </summary>
    [Theory]
    [InlineData(SpecSerializer.Json)]
    [InlineData(SpecSerializer.MessagePackNamed)]
    public void TheOtherModesDemandNothing(SpecSerializer serializer) {
        Assert.Empty(Findings(Spec(serializer, At("sensor", null), At("note", null))));
    }
}
