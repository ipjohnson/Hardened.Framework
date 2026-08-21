using Hardened.Requests.Abstract.Responses;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Requests;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// <c>[ResponseModel(...)]</c> driven through the generator rather than through the selector alone.
/// </summary>
/// <remarks>
/// <para>
/// <c>ResponseModelSelectorTests</c> covers the reading. What these cover is the half that a unit
/// test over the selector cannot: that the attribute a consumer actually writes reaches the
/// generator, and that a mode it cannot emit stops the build instead of producing an application
/// generated as though the module were Standard.
/// </para>
/// <para>
/// That distinction is the one <c>&lt;HardenedAmbiguousRoutes&gt;</c> was on the wrong side of for
/// months - read correctly by a parser whose input never arrived. An attribute is not an MSBuild
/// property and does not have that failure mode, but the assertion worth writing is the same one:
/// about the value arriving, not about the parser being right.
/// </para>
/// </remarks>
public class ResponseModelGeneratorTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(ResponseModelAttribute)
    ];

    /// <summary>
    /// A module with one ordinary handler, carrying whatever response-model attribute is given.
    /// </summary>
    private static GeneratorResult Generate(string? attribute) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    {{attribute}}
                    public partial class TestApplication { }

                    public class UserController {
                        [Get("/users/{id}")]
                        public string ById(string id) => id;
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    private static Diagnostic? Reported(GeneratorResult result, string id) =>
        result.GeneratorDiagnostics.FirstOrDefault(diagnostic => diagnostic.Id == id);

    /// <summary>
    /// No error at all. Every mode is emitted now, so a module declaring one has nothing to be told.
    /// </summary>
    private static void AssertNoModeDiagnostics(GeneratorResult result) =>
        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

    #region the modes that build

    /// <summary>
    /// Every application that has never heard of this attribute keeps building exactly as it did.
    /// </summary>
    [Fact]
    public void AnEntryPointWithNoAttribute_BuildsWithNoDiagnostic() {
        AssertNoModeDiagnostics(Generate(attribute: null));
    }

    /// <summary>
    /// Saying Standard is a choice, and a choice that works. It must not be told anything.
    /// </summary>
    [Fact]
    public void AnExplicitStandard_BuildsWithNoDiagnostic() {
        AssertNoModeDiagnostics(Generate(
            "[Hardened.Requests.Abstract.Responses.ResponseModel(" +
            "Hardened.Requests.Abstract.Responses.ResponseModel.Standard)]"));
    }

    /// <summary>
    /// Standard mode still emits a routing table, which is what says the attribute did not
    /// disturb anything on the path that already worked.
    /// </summary>
    [Fact]
    public void AnExplicitStandard_StillEmitsTheRoutingTable() {
        var result = Generate(
            "[Hardened.Requests.Abstract.Responses.ResponseModel(" +
            "Hardened.Requests.Abstract.Responses.ResponseModel.Standard)]");

        Assert.Contains(result.GeneratedSources, pair => pair.Key.Contains("Routing"));
    }

    #endregion

    #region the modes that do not

    /// <summary>
    /// Both modes build. Each reported a "not implemented" error until the work that emits it
    /// landed - HRDRM001 until code-first Response, HRDRM002 until CSharpAuthor gained the union
    /// keyword - and these are the assertions that say the diagnostics went away rather than the
    /// modes quietly doing nothing: a module declaring either produces a routing table and no error.
    /// </summary>
    [Theory]
    [InlineData("Response")]
    [InlineData("Union")]
    public void EveryMode_Builds(string mode) {
        var result = Generate(
            "[Hardened.Requests.Abstract.Responses.ResponseModel(" +
            $"Hardened.Requests.Abstract.Responses.ResponseModel.{mode})]");

        AssertNoModeDiagnostics(result);
        Assert.Contains(result.GeneratedSources, pair => pair.Key.Contains("Routing"));
    }

    #endregion
}
