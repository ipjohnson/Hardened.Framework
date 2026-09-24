using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// <c>@hardened.api#validation</c>, which is how a Smithy model says whether an operation's
/// validation stops at its first failure.
/// </summary>
/// <remarks>
/// The trait is an enum defined in <c>hardened.smithy</c>, so the Smithy CLI refuses a value it
/// does not hold before this parser sees the model. The AST carries the value as a string, which is
/// what these models hand the parser.
/// </remarks>
public class SmithyValidationTraitTests
{
    private static string Model(string trait) =>
        $$"""
            { "smithy": "2.0", "shapes": {
                "com.example#Orders": {
                  "type": "service", "version": "1",
                  "operations": [ { "target": "com.example#PlaceOrder" } ] },
                "com.example#PlaceOrder": {
                  "type": "operation",
                  "traits": {
                    {{trait}}
                    "smithy.api#http": { "method": "POST", "uri": "/orders", "code": 200 } } } } }
            """;

    private static OperationModel Operation(string trait, out List<string> diagnostics)
    {
        diagnostics = new List<string>();

        var model = SmithySpecParser.Parse(Model(trait), "orders", diagnostics);

        Assert.NotNull(model);

        return Assert.Single(model!.Services.SelectMany(service => service.Operations));
    }

    [Fact]
    public void StopOnFirstErrorNamesTheMember()
    {
        var operation = Operation(
            "\"hardened.api#validation\": \"stop-on-first-error\",",
            out var diagnostics
        );

        Assert.Equal("StopOnFirstError", operation.ValidationMode);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CollectAllNamesTheMember()
    {
        Assert.Equal(
            "CollectAll",
            Operation("\"hardened.api#validation\": \"collect-all\",", out _).ValidationMode
        );
    }

    [Fact]
    public void AnOperationDeclaringNoModeCarriesNone()
    {
        Assert.Null(Operation("", out _).ValidationMode);
    }

    /// <summary>
    /// Only reachable from an AST the CLI did not produce, and reported rather than read as no
    /// declaration, because that would report every failure without a word.
    /// </summary>
    [Fact]
    public void AValueTheTraitDoesNotHoldIsReportedNamingTheOperation()
    {
        var operation = Operation(
            "\"hardened.api#validation\": \"sometimes\",",
            out var diagnostics
        );

        Assert.Null(operation.ValidationMode);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("PlaceOrder"));
    }
}
