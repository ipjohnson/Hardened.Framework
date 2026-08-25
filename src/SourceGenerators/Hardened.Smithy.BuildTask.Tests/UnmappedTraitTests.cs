using System.Collections.Generic;
using System.Linq;
using Hardened.Idl;
using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// A trait the model declares that the parser does not map, said out loud.
/// </summary>
/// <remarks>
/// <para>
/// The Smithy half of the same rule the OpenAPI parser applies. <c>@uniqueItems</c> and
/// <c>@sparse</c> are both named in <c>SmithyTraits.Mapped</c> - the parser declares them as traits
/// it handles - and then <c>ReadConstraints</c> asks for <c>@length</c>, <c>@range</c> and
/// <c>@pattern</c> and nothing else. Being on that list and unread is precisely the gap.
/// </para>
/// <para>
/// <b>Written as an inline AST rather than a fixture.</b> The checked-in fixtures come from the real
/// CLI, and the only <c>@uniqueItems</c> among them is on a dependency's list shape rather than a
/// member of anybody's model - which the member-level check correctly does not see. Generating a new
/// fixture needs a CLI whose version is pinned away from the one on this machine, so the AST is
/// spelled out here instead. It is the minimum the parser accepts: a service, an operation with an
/// <c>@http</c> binding, and an input structure.
/// </para>
/// </remarks>
public class UnmappedTraitTests {

    /// <summary>
    /// <c>tags</c> carries <c>@uniqueItems</c> and <c>labels</c> carries <c>@sparse</c>;
    /// <c>name</c> carries <c>@length</c>, which the parser does map, so it is the control.
    /// </summary>
    private const string Ast = """
        {
          "smithy": "2.0",
          "shapes": {
            "com.example.depot#Depot": {
              "type": "service",
              "version": "2024-01-01",
              "operations": [{ "target": "com.example.depot#CreateProduct" }]
            },
            "com.example.depot#CreateProduct": {
              "type": "operation",
              "input": { "target": "com.example.depot#CreateProductInput" },
              "traits": {
                "smithy.api#http": { "method": "POST", "uri": "/products", "code": 201 }
              }
            },
            "com.example.depot#CreateProductInput": {
              "type": "structure",
              "members": {
                "name": {
                  "target": "smithy.api#String",
                  "traits": { "smithy.api#length": { "min": 1, "max": 64 } }
                },
                "tags": {
                  "target": "com.example.depot#TagList",
                  "traits": { "smithy.api#uniqueItems": {} }
                },
                "labels": {
                  "target": "com.example.depot#TagList",
                  "traits": { "smithy.api#sparse": {} }
                }
              },
              "traits": { "smithy.api#input": {} }
            },
            "com.example.depot#TagList": {
              "type": "list",
              "member": { "target": "smithy.api#String" }
            }
          }
        }
        """;

    private static ServiceSpecModel Parse(string ast) {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(ast, "depot", diagnostics);

        Assert.NotNull(model);

        return model!;
    }

    [Fact]
    public void UniqueItemsOnAMemberIsRecordedAsUnmapped() {
        Assert.Contains(Parse(Ast).UnmappedKeywords, u => u.Keyword == "@uniqueItems");
    }

    [Fact]
    public void SparseOnAMemberIsRecordedAsUnmapped() {
        Assert.Contains(Parse(Ast).UnmappedKeywords, u => u.Keyword == "@sparse");
    }

    /// <summary>
    /// Named as the model spells it, <c>@uniqueItems</c> rather than <c>uniqueItems</c>, because a
    /// reader searching their own file is looking for the form with the at-sign.
    /// </summary>
    [Fact]
    public void TheTraitIsNamedAsSmithySpellsIt() {
        Assert.All(
            Parse(Ast).UnmappedKeywords,
            unmapped => Assert.StartsWith("@", unmapped.Keyword));
    }

    [Fact]
    public void TheLocationNamesTheDeclaringMember() {
        var locations = Parse(Ast).UnmappedKeywords
            .Where(u => u.Keyword == "@uniqueItems")
            .Select(u => u.Location)
            .Distinct();

        Assert.Equal("tags", Assert.Single(locations));
    }

    /// <summary>
    /// One member declaring a trait is one place, however many times the parser met it - and it
    /// meets each one twice, because an operation's input is walked for the schema and walked again
    /// for the request body's properties. The raw notes carry the repeat; the message must not.
    /// </summary>
    [Fact]
    public void AMemberMetTwiceIsCountedOnce() {
        var problem = SpecDiagnostics.Find(Parse(Ast))
            .First(p => p.Code == "HOAT013" && p.Message.Contains("@uniqueItems"));

        Assert.Contains("at tags", problem.Message);
        Assert.DoesNotContain("other place", problem.Message);
    }

    /// <summary>
    /// A trait the parser does map produces nothing, or the diagnostic would be noise on every
    /// model that constrains anything.
    /// </summary>
    [Fact]
    public void AMappedTraitIsNotReported() {
        Assert.DoesNotContain(Parse(Ast).UnmappedKeywords, u => u.Keyword.Contains("length"));
    }

    /// <summary>
    /// It reaches the same reporting pass the OpenAPI front end uses, which is the point of putting
    /// the note on the model rather than in either parser.
    /// </summary>
    [Fact]
    public void ItReachesTheSharedDiagnosticsPass() {
        var problems = SpecDiagnostics.Find(Parse(Ast)).Where(p => p.Code == "HOAT013").ToList();

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.False(problem.Fatal));
    }

    /// <summary>
    /// A model declaring neither is silent. Without this the check could pass everything above by
    /// firing unconditionally.
    /// </summary>
    [Fact]
    public void AModelDeclaringNeitherIsSilent() {
        var clean = Ast
            .Replace("\"smithy.api#uniqueItems\": {}", "\"smithy.api#required\": {}")
            .Replace("\"smithy.api#sparse\": {}", "\"smithy.api#required\": {}");

        // The replacements have to have happened, or this asserts nothing.
        Assert.DoesNotContain("uniqueItems", clean);
        Assert.DoesNotContain("sparse", clean);

        Assert.Empty(Parse(clean).UnmappedKeywords);
    }
}
