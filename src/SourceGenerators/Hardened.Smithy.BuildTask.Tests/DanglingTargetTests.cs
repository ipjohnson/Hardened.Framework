using Hardened.Generation.Models;
using Hardened.Idl;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// A target the model references and does not declare.
/// </summary>
/// <remarks>
/// <para>
/// The Smithy half of the OpenAPI front end's dangling <c>$ref</c>. These were warnings that named
/// what the parser did instead - <c>JsonElement</c> for a member, a dropped operation - and one of
/// them, an error shape bound to an operation, said nothing at all: the interface simply never
/// declared that error and the status the model promised was answered by nothing.
/// </para>
/// <para>
/// The CLI refuses an undeclared target before this parser ever sees it, so a model built from
/// sources cannot reach here. A committed AST can, which is exactly the case with no other check
/// in front of it.
/// </para>
/// </remarks>
public class DanglingTargetTests {

    private static ServiceSpecModel Parse(string shapes) {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(
            $$"""
              { "smithy": "2.0", "shapes": { {{shapes}} } }
              """, "depot", diagnostics);

        Assert.NotNull(model);

        return model!;
    }

    private static SpecDiagnostics.Problem[] Problems(ServiceSpecModel model) =>
        SpecDiagnostics.Find(model, "HSMT").ToArray();

    private const string Service = """
        "com.example#Svc": {
          "type": "service", "version": "1",
          "operations": [ { "target": "com.example#Op" } ] }
        """;

    /// <summary>An error shape bound to an operation that the model never declares.</summary>
    [Fact]
    public void AnUndeclaredErrorShapeIsRecorded() {
        var model = Parse($$"""
            {{Service}},
            "com.example#Op": {
              "type": "operation",
              "errors": [ { "target": "com.example#NoSuchError" } ],
              "traits": { "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 } } }
            """);

        var dangling = Assert.Single(model.DanglingReferences);

        Assert.Equal("com.example#NoSuchError", dangling.Reference);
        Assert.Contains("declares error", dangling.Location);
    }

    [Fact]
    public void AnUndeclaredInputShapeIsRecorded() {
        var model = Parse($$"""
            {{Service}},
            "com.example#Op": {
              "type": "operation",
              "input": { "target": "com.example#NoSuchInput" },
              "traits": { "smithy.api#http": { "method": "POST", "uri": "/x", "code": 200 } } }
            """);

        Assert.Equal(
            "com.example#NoSuchInput", Assert.Single(model.DanglingReferences).Reference);
    }

    [Fact]
    public void AnUndeclaredOutputShapeIsRecorded() {
        var model = Parse($$"""
            {{Service}},
            "com.example#Op": {
              "type": "operation",
              "output": { "target": "com.example#NoSuchOutput" },
              "traits": { "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 } } }
            """);

        Assert.Equal(
            "com.example#NoSuchOutput", Assert.Single(model.DanglingReferences).Reference);
    }

    /// <summary>
    /// An operation the service binds and the model never declares, beside one it does. A service
    /// with no readable operation at all fails earlier, as a model that describes no service.
    /// </summary>
    private const string OneGoodOneMissing = """
        "com.example#Svc": {
          "type": "service", "version": "1",
          "operations": [
            { "target": "com.example#Good" },
            { "target": "com.example#Missing" } ] },
        "com.example#Good": {
          "type": "operation",
          "traits": { "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 } } }
        """;

    [Fact]
    public void AnUndeclaredOperationIsRecorded() {
        var model = Parse(OneGoodOneMissing);

        var dangling = Assert.Single(model.DanglingReferences);

        Assert.Equal("com.example#Missing", dangling.Reference);
        Assert.Contains("binds operation", dangling.Location);
    }

    /// <summary>It stops the build, under the front end that read the model.</summary>
    [Fact]
    public void ItStopsTheBuildUnderTheSmithyPrefix() {
        var problem = Assert.Single(
            Problems(Parse(OneGoodOneMissing)), p => p.Code == "HSMT027");

        Assert.True(problem.Fatal);
        Assert.Contains("com.example#Missing", problem.Message);
    }

    /// <summary>A model that declares what it references says nothing.</summary>
    [Fact]
    public void AModelThatResolvesIsSilent() {
        var model = Parse($$"""
            {{Service}},
            "com.example#Op": {
              "type": "operation",
              "output": { "target": "com.example#Out" },
              "traits": { "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 } } },
            "com.example#Out": {
              "type": "structure",
              "members": { "sku": { "target": "smithy.api#String" } } }
            """);

        Assert.Empty(model.DanglingReferences);
        Assert.DoesNotContain(Problems(model), p => p.Code == "HSMT027");
    }
}
