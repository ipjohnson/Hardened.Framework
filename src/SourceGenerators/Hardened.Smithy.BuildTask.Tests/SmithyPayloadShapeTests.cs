using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// What an <c>@httpPayload</c> member's target becomes, which used to depend on nothing but the
/// shape id.
/// </summary>
/// <remarks>
/// <para>
/// <c>ParseOutput</c> resolved the target with <c>ReferenceTo</c>, which builds a schema for a shape
/// id and is right only for the kinds that get a C# type of their own. <c>Describe</c> is the
/// function that decides which those are, and its own remarks state the rule: structures, unions and
/// enums become references, and everything else is inlined at the use site - "a named <c>list</c> is
/// a <c>List&lt;T&gt;</c>".
/// </para>
/// <para>
/// So a list target reached <c>BuildSchema</c>, whose default arm assumes an object and reads a
/// <c>members</c> map. A list shape carries a singular <c>member</c> and no map at all, so the loop
/// ran zero times and <c>list PetList { member: Pet }</c> became <c>record PetList;</c> with no
/// members - a service interface asking for a type there was no way to fill. The same shape read as
/// a structure member has always inlined to <c>List&lt;Pet&gt;</c>, so one build emitted both.
/// </para>
/// <para>
/// Nothing reported it. That arm has no diagnostic for a shape kind it does not model, which is why
/// the map case below asserts one now.
/// </para>
/// </remarks>
public class SmithyPayloadShapeTests {

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "payload-shapes.json"));

    private static ServiceSpecModel Model(List<string> diagnostics) {
        var model = SmithySpecParser.Parse(Fixture(), "payload-shapes", diagnostics);

        Assert.NotNull(model);

        return model!;
    }

    private static OperationModel Operation(string operationId) =>
        Model(new List<string>()).Services
            .SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == operationId);

    /// <summary>
    /// A list of a structure is an array of that structure, not a type named after the list.
    /// </summary>
    [Fact]
    public void AListPayloadIsAnArrayOfItsMember() {
        var operation = Operation("ListPets");

        Assert.True(operation.ResponseIsArray);
        Assert.Equal("#/components/schemas/Pet", operation.ResponseArrayItemsRef);
        Assert.Null(operation.ResponseRef);
    }

    /// <summary>
    /// And no schema is written for the list shape itself.
    /// </summary>
    /// <remarks>
    /// The assertion that pins the defect rather than its symptom. <c>PetList</c> used to be emitted
    /// as an object schema with no properties, so a build produced both <c>record PetList;</c> and,
    /// from the same shape used as a structure member, <c>List&lt;Pet&gt;</c>.
    /// </remarks>
    [Fact]
    public void AListPayloadWritesNoSchemaOfItsOwn() {
        Assert.DoesNotContain(
            Model(new List<string>()).Schemas, schema => schema.Name == "PetList");
    }

    /// <summary>
    /// A list of a prelude type names the type rather than a reference, which is what
    /// <c>List&lt;string&gt;</c> is generated from.
    /// </summary>
    [Fact]
    public void AListPayloadOfPrimitivesNamesTheItemType() {
        var operation = Operation("ListNames");

        Assert.True(operation.ResponseIsArray);
        Assert.Equal("string", operation.ResponseArrayItemsType);
        Assert.Null(operation.ResponseArrayItemsRef);
    }

    /// <summary>
    /// A structure payload is unchanged, which is the case that always worked and the reason the
    /// defect went unnoticed.
    /// </summary>
    [Fact]
    public void AStructurePayloadIsStillAReference() {
        var operation = Operation("GetPet");

        Assert.Equal("#/components/schemas/Pet", operation.ResponseRef);
        Assert.False(operation.ResponseIsArray);
    }

    /// <summary>
    /// A prelude target is the type it maps to.
    /// </summary>
    /// <remarks>
    /// Worse than the list case before this. <c>ReferenceTo</c> found no shape for
    /// <c>smithy.api#String</c>, so <c>BuildSchema</c> added nothing and the operation was left
    /// holding a reference to a type that was never written.
    /// </remarks>
    [Fact]
    public void APreludePayloadIsTheTypeItMapsTo() {
        var operation = Operation("GetName");

        Assert.Equal("string", operation.ResponseType);
        Assert.Null(operation.ResponseRef);
    }

    /// <summary>
    /// A map payload has nowhere to land, and says so.
    /// </summary>
    /// <remarks>
    /// The shared model carries <c>ResponseIsArray</c> and its item type and nothing equivalent for
    /// a dictionary, so the emitters fall back to a <c>Task</c> with no result. That is not what a
    /// model declaring a payload asked for, and the whole lesson of the list case is that this arm
    /// used to be silent.
    /// </remarks>
    [Fact]
    public void AMapPayloadIsReported() {
        var diagnostics = new List<string>();

        Model(diagnostics);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Contains("GetTags") && diagnostic.Contains("@httpPayload"));
    }

    /// <summary>And leaves nothing behind that would name a body it cannot describe.</summary>
    [Fact]
    public void AMapPayloadNamesNoResponseShape() {
        var operation = Operation("GetTags");

        Assert.Null(operation.ResponseRef);
        Assert.Null(operation.ResponseType);
        Assert.False(operation.ResponseIsArray);
    }
}
