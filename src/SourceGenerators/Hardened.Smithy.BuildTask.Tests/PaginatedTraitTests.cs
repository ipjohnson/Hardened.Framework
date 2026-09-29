using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Idl;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// <c>@paginated</c>, read and checked against the members it names.
/// </summary>
/// <remarks>
/// <para>
/// The trait was a degrade, warned as HSMT006 on every build of a model that used it. It is read
/// now, and a member it names that the operation does not have, or that has the wrong shape, is
/// HSMT035. The rules are the ones the Smithy CLI reports as errors, so the cases the CLI only
/// warns about, a map token and a long page size, build here too.
/// </para>
/// <para>
/// Inline ASTs rather than fixtures. The CLI refuses every broken case below, so a fixture built
/// from <c>.smithy</c> sources could not hold one. A committed AST can.
/// </para>
/// </remarks>
public class PaginatedTraitTests
{
    private const string FullTrait =
        "{ \"inputToken\": \"pageToken\", \"outputToken\": \"nextPageToken\", "
        + "\"pageSize\": \"pageSize\", \"items\": \"members\" }";

    /// <summary>
    /// A service over one list operation. <paramref name="operationTrait"/> is the operation's
    /// <c>@paginated</c> value, or null for none.
    /// </summary>
    private static string Ast(
        string? operationTrait,
        string? serviceTrait = null,
        string pageTokenTraits = "\"smithy.api#httpQuery\": \"pageToken\"",
        string pageSizeTarget = "smithy.api#Integer"
    ) =>
        $$"""
            {
              "smithy": "2.0",
              "shapes": {
                "com.example.staff#Staff": {
                  "type": "service",
                  "version": "2024-01-01",
                  "operations": [{ "target": "com.example.staff#ListStaff" }]
                  {{(
                serviceTrait == null
                    ? ""
                    : $", \"traits\": {{ \"smithy.api#paginated\": {serviceTrait} }}"
            )}}
                },
                "com.example.staff#ListStaff": {
                  "type": "operation",
                  "input": { "target": "com.example.staff#ListStaffInput" },
                  "output": { "target": "com.example.staff#ListStaffOutput" },
                  "traits": {
                    "smithy.api#readonly": {},
                    "smithy.api#http": { "method": "GET", "uri": "/staff", "code": 200 }
                    {{(
                operationTrait == null ? "" : $", \"smithy.api#paginated\": {operationTrait}"
            )}}
                  }
                },
                "com.example.staff#ListStaffInput": {
                  "type": "structure",
                  "members": {
                    "pageToken": {
                      "target": "smithy.api#String",
                      "traits": { {{pageTokenTraits}} }
                    },
                    "pageSize": {
                      "target": "{{pageSizeTarget}}",
                      "traits": { "smithy.api#httpQuery": "pageSize" }
                    },
                    "startKey": { "target": "com.example.staff#KeyMap" }
                  },
                  "traits": { "smithy.api#input": {} }
                },
                "com.example.staff#ListStaffOutput": {
                  "type": "structure",
                  "members": {
                    "members": { "target": "com.example.staff#MemberList" },
                    "nextPageToken": { "target": "smithy.api#String" },
                    "lastKey": { "target": "com.example.staff#KeyMap" },
                    "page": { "target": "com.example.staff#PageInfo" }
                  },
                  "traits": { "smithy.api#output": {} }
                },
                "com.example.staff#PageInfo": {
                  "type": "structure",
                  "members": {
                    "next": { "target": "smithy.api#String" },
                    "count": { "target": "smithy.api#Integer" }
                  }
                },
                "com.example.staff#MemberList": {
                  "type": "list",
                  "member": { "target": "com.example.staff#Member" }
                },
                "com.example.staff#Member": {
                  "type": "structure",
                  "members": { "id": { "target": "smithy.api#String" } }
                },
                "com.example.staff#KeyMap": {
                  "type": "map",
                  "key": { "target": "smithy.api#String" },
                  "value": { "target": "smithy.api#String" }
                }
              }
            }
            """;

    private static ServiceSpecModel Parse(string ast, List<string>? diagnostics = null)
    {
        var model = SmithySpecParser.Parse(ast, "staff", diagnostics ?? new List<string>());

        Assert.NotNull(model);

        return model!;
    }

    private static string Mismatch(string ast) =>
        Assert.Single(Parse(ast).PaginationMismatches).Detail;

    [Fact]
    public void AFullyNamedTraitBuildsWithNoDiagnostic()
    {
        var diagnostics = new List<string>();
        var model = Parse(Ast(FullTrait), diagnostics);

        Assert.Empty(model.PaginationMismatches);
        Assert.DoesNotContain(diagnostics, d => d.Contains("paginated"));
    }

    [Fact]
    public void TheServiceFillsInWhatTheOperationLeavesOut()
    {
        var model = Parse(
            Ast(
                "{ \"items\": \"members\" }",
                serviceTrait: "{ \"inputToken\": \"pageToken\", \"outputToken\": \"nextPageToken\", \"pageSize\": \"pageSize\" }"
            )
        );

        Assert.Empty(model.PaginationMismatches);
    }

    [Fact]
    public void TheOperationsSettingWinsOverTheServices()
    {
        var model = Parse(Ast(FullTrait, serviceTrait: "{ \"inputToken\": \"cursor\" }"));

        Assert.Empty(model.PaginationMismatches);
    }

    /// <summary>A service's trait only supplies settings, so an operation without its own is not paged.</summary>
    [Fact]
    public void AServiceTraitAlonePagesNothing()
    {
        var model = Parse(
            Ast(null, serviceTrait: "{ \"inputToken\": \"cursor\", \"outputToken\": \"cursor\" }")
        );

        Assert.Empty(model.PaginationMismatches);
    }

    [Fact]
    public void BothTokensHaveToBeNamed()
    {
        var mismatches = Parse(Ast("{ \"items\": \"members\" }"))
            .PaginationMismatches.Select(m => m.Detail)
            .ToList();

        Assert.Equal(
            new[]
            {
                "no inputToken is named on it or on its service",
                "no outputToken is named on it or on its service",
            },
            mismatches
        );
    }

    [Fact]
    public void AnInputTokenTheInputDoesNotHaveIsRefused()
    {
        Assert.Equal(
            "its inputToken 'cursor' names no member of its input",
            Mismatch(Ast("{ \"inputToken\": \"cursor\", \"outputToken\": \"nextPageToken\" }"))
        );
    }

    /// <summary>The trait defines an input setting as a member name, so a dot does not walk.</summary>
    [Fact]
    public void AnInputSettingIsNotAPath()
    {
        Assert.Equal(
            "its pageSize 'page.count' names no member of its input",
            Mismatch(
                Ast(
                    "{ \"inputToken\": \"pageToken\", \"outputToken\": \"nextPageToken\", \"pageSize\": \"page.count\" }"
                )
            )
        );
    }

    [Fact]
    public void AnOutputSettingIsAPathThroughNestedStructures()
    {
        var model = Parse(Ast("{ \"inputToken\": \"pageToken\", \"outputToken\": \"page.next\" }"));

        Assert.Empty(model.PaginationMismatches);
    }

    [Theory]
    [InlineData("page.nope")]
    [InlineData("members.id")]
    [InlineData("nextPageToken.length")]
    public void AnOutputPathThatLeavesTheStructuresIsRefused(string outputToken)
    {
        Assert.Equal(
            $"its outputToken '{outputToken}' names no member of its output",
            Mismatch(
                Ast($"{{ \"inputToken\": \"pageToken\", \"outputToken\": \"{outputToken}\" }}")
            )
        );
    }

    [Fact]
    public void ATokenOfAnotherTypeIsRefused()
    {
        Assert.Equal(
            "its inputToken 'pageSize' targets integer, where a token is a string or a map",
            Mismatch(Ast("{ \"inputToken\": \"pageSize\", \"outputToken\": \"nextPageToken\" }"))
        );
    }

    /// <summary>The CLI calls a map token dangerous rather than wrong, and builds it.</summary>
    [Fact]
    public void AMapTokenIsAccepted()
    {
        var model = Parse(Ast("{ \"inputToken\": \"startKey\", \"outputToken\": \"lastKey\" }"));

        Assert.Empty(model.PaginationMismatches);
    }

    [Fact]
    public void APageSizeOfAnotherTypeIsRefused()
    {
        Assert.Equal(
            "its pageSize 'pageToken' targets string, where a page size is a byte, short, integer or long",
            Mismatch(
                Ast(
                    "{ \"inputToken\": \"pageToken\", \"outputToken\": \"nextPageToken\", \"pageSize\": \"pageToken\" }"
                )
            )
        );
    }

    [Theory]
    [InlineData("smithy.api#Long")]
    [InlineData("smithy.api#PrimitiveLong")]
    [InlineData("smithy.api#Short")]
    [InlineData("smithy.api#Byte")]
    public void AnyIntegerTypeIsAPageSize(string target)
    {
        var model = Parse(Ast(FullTrait, pageSizeTarget: target));

        Assert.Empty(model.PaginationMismatches);
    }

    [Fact]
    public void ItemsThatAreNotACollectionAreRefused()
    {
        Assert.Equal(
            "its items 'page.count' targets integer, where the items are a list or a map",
            Mismatch(
                Ast(
                    "{ \"inputToken\": \"pageToken\", \"outputToken\": \"nextPageToken\", \"items\": \"page.count\" }"
                )
            )
        );
    }

    [Fact]
    public void ARequiredInputTokenIsRefused()
    {
        Assert.Equal(
            "its inputToken 'pageToken' is @required, so a first request has no token to send",
            Mismatch(
                Ast(
                    FullTrait,
                    pageTokenTraits: "\"smithy.api#httpQuery\": \"pageToken\", \"smithy.api#required\": {}"
                )
            )
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"input\": {},")]
    [InlineData("\"input\": { \"target\": \"smithy.api#Unit\" },")]
    public void AnOperationWithNoInputStructureHasNoInputMembers(string input)
    {
        var full = Ast(FullTrait);
        var ast = full.Replace(
            "\"input\": { \"target\": \"com.example.staff#ListStaffInput\" },",
            input
        );

        // The replacement has to have happened, or this asserts nothing.
        Assert.NotEqual(full, ast);

        Assert.Equal(
            new[]
            {
                "its inputToken 'pageToken' names no member of its input",
                "its pageSize 'pageSize' names no member of its input",
            },
            Parse(ast).PaginationMismatches.Select(m => m.Detail)
        );
    }

    /// <summary>
    /// A member whose target names no shape is reported by the dangling-reference check, so the
    /// paging check says nothing about its type.
    /// </summary>
    [Theory]
    [InlineData("com.example.staff#Missing")]
    [InlineData("smithy.api#")]
    public void ATargetThatNamesNoShapeIsLeftToTheDanglingReferenceCheck(string target)
    {
        Assert.Empty(Parse(Ast(FullTrait, pageSizeTarget: target)).PaginationMismatches);
    }

    [Fact]
    public void AMismatchIsAnHsmt035ErrorNamingTheOperation()
    {
        var model = Parse(
            Ast("{ \"inputToken\": \"cursor\", \"outputToken\": \"nextPageToken\" }")
        );

        var problem = Assert.Single(SpecDiagnostics.Find(model, "HSMT"), p => p.Code == "HSMT035");

        Assert.True(problem.Fatal);
        Assert.StartsWith(
            "Operation 'ListStaff' is @paginated, and its inputToken 'cursor' names no member of its input.",
            problem.Message
        );
    }

    /// <summary>
    /// The trait was a degrade, warned as HSMT006 on every build of a model that used it, so a model
    /// that paged could not build warning-free.
    /// </summary>
    [Fact]
    public void TheTraitIsNoLongerReportedAsHavingNoEquivalent()
    {
        var diagnostics = new List<string>();

        Parse(Ast(FullTrait, serviceTrait: "{ \"pageSize\": \"pageSize\" }"), diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Contains("smithy.api#paginated"));
    }
}
