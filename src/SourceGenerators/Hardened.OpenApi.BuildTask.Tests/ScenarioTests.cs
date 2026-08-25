using System.Collections.Generic;
using System.Threading;
using Hardened.Idl;
using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// What each document in <see cref="ScenarioSpecs"/> produces.
/// </summary>
/// <remarks>
/// Assertions written from what the generator actually emits, checked against a dump rather than
/// guessed - twice in this file's history an expectation was written from what the code looked like
/// it should do and passed against output that was wrong in a different way.
/// </remarks>
public class ScenarioTests {

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "spec", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static PropertyModel Property(ServiceSpecModel model, string schema, string property) {
        var found = Assert.Single(model.Schemas, candidate => candidate.Name == schema);

        return Assert.Single(found.Properties, candidate => candidate.Name == property);
    }

    private static string TypeOf(ServiceSpecModel model, string schema, string property) =>
        TypeMapper.MapPropertyToCSharpType(Property(model, schema, property));

    /// <summary>
    /// 3.1 spells nullability as a type array, and it is one property either way.
    /// </summary>
    /// <remarks>
    /// Read as a choice between two things it became a two-branch union - which turned every
    /// optional string in OpenAI's description into a generated type, 596 of them against 92 real
    /// ones.
    /// </remarks>
    [Fact]
    public void ATypeArrayEndingInNullIsANullablePropertyRatherThanAChoice() {
        var model = Parse(ScenarioSpecs.NullableByTypeArray);

        Assert.Equal("string", TypeOf(model, "Thing", "name"));
        Assert.Equal("int", TypeOf(model, "Thing", "count"));

        Assert.True(Property(model, "Thing", "name").IsNullable);
        Assert.DoesNotContain(model.Schemas, schema => schema.Kind == SchemaKind.OneOf);
    }

    /// <summary>
    /// <c>const</c> is an enum of one, and is how 3.1 pins a value.
    /// </summary>
    /// <remarks>
    /// Never read at all until this test was written: a pinned property looked unconstrained, and
    /// the choice resolution that uses one to tell branches apart could not fire because nothing
    /// put a pinned value in the model.
    /// </remarks>
    [Fact]
    public void AConstIsReadAsTheSingleValueItPermits() {
        var model = Parse(ScenarioSpecs.ConstProperty);

        var kind = Property(model, "Thing", "kind");

        Assert.NotNull(kind.EnumValues);
        Assert.Equal(new[] { "thing" }, kind.EnumValues!);
    }

    /// <summary>
    /// A declared bound wider than the type its format implies widens the type.
    /// </summary>
    /// <remarks>
    /// DigitalOcean declares an integer with a maximum of 4294967295. Left as <c>int</c> the model
    /// overflows on a payload the description calls valid.
    /// </remarks>
    [Fact]
    public void ABoundTooWideForIntWidensTheTypeThatCarriesIt() {
        var model = Parse(ScenarioSpecs.BoundsWiderThanInt);

        Assert.Equal("int", TypeOf(model, "Thing", "small"));
        Assert.Equal("long", TypeOf(model, "Thing", "wide"));
    }

    /// <summary>
    /// Awkward values reach C# as names, and as different ones.
    /// </summary>
    /// <remarks>
    /// GitHub's reaction enum is <c>+1</c> and <c>-1</c>, Docker and Cloudflare declare an empty
    /// member, and Elasticsearch declares <c>buckets.count</c> beside <c>buckets_count</c> - which
    /// sanitize to one name. Each produced a member with no identifier, or two with the same one.
    /// </remarks>
    [Fact]
    public void EveryEnumValueBecomesADistinctName() {
        var model = Parse(ScenarioSpecs.AwkwardEnumValues);

        var reaction = Assert.Single(model.Schemas, schema => schema.Name == "Reaction");

        Assert.Equal(
            new[] {
                "Plus1", "Minus1", "Empty", "BucketsCount", "ReactionBucketsCount",
                "StartTimeGreaterThan"
            },
            reaction.EnumMembers);

        // The wire values are untouched - only the C# member moved.
        Assert.Contains("+1", reaction.EnumValues);
        Assert.Contains("", reaction.EnumValues);
    }

    [Fact]
    public void BinaryAndDateFormatsMapToTheTypesThatHoldThem() {
        var model = Parse(ScenarioSpecs.BinaryFormats);

        Assert.Equal("byte[]", TypeOf(model, "Thing", "avatar"));
        Assert.Equal("byte[]", TypeOf(model, "Thing", "blob"));
        Assert.Equal("DateTimeOffset", TypeOf(model, "Thing", "when"));
        Assert.Equal("DateOnly", TypeOf(model, "Thing", "day"));
    }

    /// <summary>
    /// A schema that refers to itself parses, and an inline object is lifted into a type.
    /// </summary>
    /// <remarks>
    /// A recursive reference is a cycle in every pass that walks references. The array of arrays is
    /// here to record what happens rather than to approve of it - see the assertion.
    /// </remarks>
    [Fact]
    public void ARecursiveSchemaParsesAndAnInlineObjectIsLifted() {
        var model = Parse(ScenarioSpecs.RecursiveAndNested);

        Assert.Equal("Thing", TypeOf(model, "Thing", "self"));
        Assert.Equal("List<Thing>", TypeOf(model, "Thing", "children"));

        // Lifted, so the nested shape survives instead of degrading to JsonElement.
        Assert.Contains(model.Schemas, schema => schema.Name == "ThingInline");

        // Known: an array of arrays loses its inner element type. Pinned so that fixing it is a
        // deliberate change to this line rather than a surprise.
        Assert.Equal("List<JsonElement>", TypeOf(model, "Thing", "matrix"));
    }

    /// <summary>A body that is not JSON is still a body.</summary>
    [Fact]
    public void AMultipartBodyIsReadRatherThanSkipped() {
        var model = Parse(ScenarioSpecs.MultipartBody);

        var operation = Assert.Single(model.Services[0].Operations);

        Assert.Equal("multipart/form-data", operation.RequestBodyContentType);
        Assert.Equal("object", operation.RequestBodyType);
    }

    /// <summary>
    /// A document declaring webhooks parses, and the operations it declares are unaffected.
    /// </summary>
    /// <remarks>
    /// Webhooks are not generated. This says what happens today, so that generating them is a
    /// change someone makes on purpose and this test is what tells them what moved.
    /// </remarks>
    [Fact]
    public void WebhooksAreIgnoredWithoutDisturbingTheRestOfTheDocument() {
        var model = Parse(ScenarioSpecs.Webhooks);

        var operation = Assert.Single(model.Services[0].Operations);

        Assert.Equal("GetThing", operation.MethodName);
        Assert.Contains(model.Schemas, schema => schema.Name == "Thing");
    }

    /// <summary>
    /// Every scenario document parses at all.
    /// </summary>
    /// <remarks>
    /// The cheapest thing this file does and the one most likely to catch the next regression: a
    /// pass that throws on a shape it has not met before fails here, on a document small enough to
    /// read, rather than in a megabyte of generated C# from a vendor's description.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryScenario))]
    public void EveryScenarioParses(string name, string yaml) {
        var diagnostics = new List<string>();

        var model = OpenApiSpecParser.Parse(
            yaml, "spec", CancellationToken.None, diagnostics: diagnostics);

        Assert.True(model != null, $"{name} did not parse: {string.Join("; ", diagnostics)}");
        Assert.Empty(diagnostics);
    }

    public static IEnumerable<object[]> EveryScenario() {
        foreach (var field in typeof(ScenarioSpecs).GetFields(
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.Static)) {
            if (field.GetRawConstantValue() is string yaml) {
                yield return new object[] { field.Name, yaml };
            }
        }
    }
}
