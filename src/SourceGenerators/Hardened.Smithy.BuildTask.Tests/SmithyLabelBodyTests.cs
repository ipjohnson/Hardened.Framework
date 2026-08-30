using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// An input with both bindings and a body: the bound members leave the body entirely.
/// </summary>
/// <remarks>
/// Written the obvious way - one <c>@httpLabel</c> member and the rest unbound - the request body
/// was the whole input structure, label included and required there, so
/// <c>PUT /things/{id}</c> with the id only in the path answered
/// <c>{"field":"body.id","code":"required"}</c> and the published document agreed with the wrong
/// behaviour. The body is now a derived schema of the unbound members alone.
/// </remarks>
public class SmithyLabelBodyTests {

    private const string Model =
        """
        { "smithy": "2.0", "shapes": {
            "com.example#Svc": {
              "type": "service", "version": "1",
              "operations": [ { "target": "com.example#Replace" } ] },
            "com.example#Replace": {
              "type": "operation",
              "traits": { "smithy.api#http": { "method": "PUT", "uri": "/things/{id}", "code": 200 } },
              "input": { "target": "com.example#ReplaceInput" } },
            "com.example#ReplaceInput": {
              "type": "structure",
              "members": {
                "id": { "target": "smithy.api#String",
                        "traits": { "smithy.api#httpLabel": {}, "smithy.api#required": {} } },
                "name": { "target": "smithy.api#String",
                          "traits": { "smithy.api#required": {} } },
                "note": { "target": "smithy.api#String" } } } } }
        """;

    private static ServiceSpecModel Parse() {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(Model, "labelbody", diagnostics);

        Assert.NotNull(model);

        return model!;
    }

    [Fact]
    public void TheLabelIsAPathParameterAndNotInTheBody() {
        var model = Parse();
        var operation = Assert.Single(Assert.Single(model.Services).Operations);

        var label = Assert.Single(operation.Parameters);

        Assert.Equal("id", label.Name);
        Assert.Equal("path", label.In);

        Assert.Equal("#/components/schemas/ReplaceInputBody", operation.RequestBodyRef);
        Assert.DoesNotContain("id", operation.RequestBodyRequired);
        Assert.Contains("name", operation.RequestBodyRequired);
    }

    [Fact]
    public void TheDerivedBodySchemaCarriesOnlyTheUnboundMembers() {
        var model = Parse();
        var body = model.Schemas.Find(schema => schema.Name == "ReplaceInputBody");

        Assert.NotNull(body);
        Assert.Equal(
            new[] { "name", "note" },
            body!.Properties.Select(property => property.Name).OrderBy(name => name));
        Assert.Equal(new[] { "name" }, body.Required);
    }
}
