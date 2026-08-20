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

    private static void AssertNoModeDiagnostics(GeneratorResult result) {
        Assert.Null(Reported(result, ResponseModelDiagnostics.ResponseNotImplementedId));
        Assert.Null(Reported(result, ResponseModelDiagnostics.UnionNotImplementedId));
    }

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
    /// The whole point of C.2. Accepting this and emitting standard-mode handlers produces an
    /// application that compiles, runs and answers every request the way standard mode does, with
    /// the declared response set discarded and nothing saying so.
    /// </summary>
    [Fact]
    public void UnionMode_IsABuildError() {
        var reported = Reported(
            Generate(
                "[Hardened.Requests.Abstract.Responses.ResponseModel(" +
                "Hardened.Requests.Abstract.Responses.ResponseModel.Union)]"),
            ResponseModelDiagnostics.UnionNotImplementedId);

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Error, reported!.Severity);
        Assert.Contains("TestApplication", reported.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseMode_IsABuildError() {
        var reported = Reported(
            Generate(
                "[Hardened.Requests.Abstract.Responses.ResponseModel(" +
                "Hardened.Requests.Abstract.Responses.ResponseModel.Response)]"),
            ResponseModelDiagnostics.ResponseNotImplementedId);

        Assert.NotNull(reported);
        Assert.Equal(DiagnosticSeverity.Error, reported!.Severity);
        Assert.Contains("TestApplication", reported.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// One mode reports one diagnostic. Reporting both would leave a consumer suppressing the wrong
    /// one, and would say the module declared something it did not.
    /// </summary>
    [Fact]
    public void UnionMode_DoesNotAlsoReportTheResponseDiagnostic() {
        var result = Generate(
            "[Hardened.Requests.Abstract.Responses.ResponseModel(" +
            "Hardened.Requests.Abstract.Responses.ResponseModel.Union)]");

        Assert.Null(Reported(result, ResponseModelDiagnostics.ResponseNotImplementedId));
    }

    #endregion
}
