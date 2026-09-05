using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// A union under <c>@streaming</c>, which is Smithy's own spelling for an event stream, read as
/// the streamed response it is: the union is the item, framed as server-sent events.
/// </summary>
/// <remarks>
/// Before this the trait was in no table, so it was reported as one this generator does not
/// model and the payload was read as a single JSON object: a <c>Task&lt;PetEventStream&gt;</c>
/// on the interface and one document where the model promised many.
/// </remarks>
public class SmithyEventStreamTests {

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "event-stream.json"));

    private static ServiceSpecModel Model(List<string> diagnostics) {
        var model = SmithySpecParser.Parse(Fixture(), "event-stream", diagnostics);

        Assert.NotNull(model);

        return model!;
    }

    private static OperationModel Operation(string operationId) =>
        Model(new List<string>()).Services
            .SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == operationId);

    [Fact]
    public void AStreamingUnionPayloadIsTheItemOfAStream() {
        var operation = Operation("PetEvents");

        Assert.Equal("#/components/schemas/PetEventStream", operation.ItemSchemaRef);
        Assert.Equal("text/event-stream", operation.ResponseContentType);

        // The item schema is what a stream has instead of a response schema, not as well as.
        Assert.Null(operation.ResponseRef);
        Assert.False(operation.ResponseIsArray);
    }

    [Fact]
    public void AnUnstreamedOperationIsUntouched() {
        var operation = Operation("GetPet");

        Assert.Null(operation.ItemSchemaRef);
        Assert.Equal("#/components/schemas/Pet", operation.ResponseRef);
    }

    /// <summary>
    /// Each member's wire name rides with its branch, which is what becomes the <c>event:</c>
    /// field; <c>@jsonName</c> renames it the way it renames a property.
    /// </summary>
    [Fact]
    public void TheUnionsMembersKeepTheirNames() {
        var union = Model(new List<string>()).Schemas.Single(schema => schema.Name == "PetEventStream");

        Assert.Equal(SchemaKind.OneOf, union.Kind);
        Assert.Equal(
            new[] { "#/components/schemas/PetAdopted as adopted", "#/components/schemas/PetWeighed as weighed-in" },
            union.OneOf.Select(branch => branch.Ref + " as " + branch.Name));
    }

    [Fact]
    public void StreamingIsAModelledTrait() {
        var diagnostics = new List<string>();

        Model(diagnostics);

        Assert.DoesNotContain(diagnostics, line => line.Contains("smithy.api#streaming"));
    }
}
