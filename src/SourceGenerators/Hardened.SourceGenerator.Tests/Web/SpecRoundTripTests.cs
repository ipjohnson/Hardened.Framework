using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// A code-first handler model, described as a spec and rebuilt from it, is the model that went in.
/// </summary>
/// <remarks>
/// <para>
/// This is the question the whole pipeline unification turns on: can one spec model carry what all
/// three front-ends express? For the described front-ends the answer is yes by construction — it is
/// their own model. For code-first it is a claim, and this is where it gets checked.
/// </para>
/// <para>
/// A failure here names something a description cannot say about a code-first handler. That is a
/// finding about the model, not a bug in the projection: the fix is either a field on the spec model
/// or a member on <c>OperationSymbols</c>, and which one it should be is the interesting part.
/// </para>
/// </remarks>
public class SpecRoundTripTests {

    [Theory]
    [MemberData(nameof(Corpus))]
    public void HandlerModel_SurvivesBeingDescribedAndRebuilt(string scenario) {
        var original = CodeFirstModels(scenario);

        Assert.NotEmpty(original);

        var (spec, symbols) = CodeFirstSpecProjection.Project(original);

        var rebuilt = SpecHandlerModelBuilder.BuildModels(
            spec, "TestApp.Models", "TestApp.Services", "TestApp.Generated", "TestApp.Validation",
            symbols);

        Assert.Equal(original.Count, rebuilt.Count);

        foreach (var before in original) {
            var after = rebuilt.SingleOrDefault(candidate =>
                candidate.Name.Path == before.Name.Path &&
                candidate.Name.Method == before.Name.Method);

            Assert.True(after != null,
                $"{scenario}: {before.Name.Method} {before.Name.Path} did not survive the round trip.");

            AssertSame(scenario, before, after!);
        }
    }

    private static void AssertSame(string scenario, RequestHandlerModel before, RequestHandlerModel after) {
        string Where(string what) => $"{scenario}: {before.Name.Method} {before.Name.Path} — {what}";

        Assert.True(before.ControllerType.Name == after.ControllerType.Name,
            Where($"declaring type {before.ControllerType.Name} became {after.ControllerType.Name}"));

        Assert.True(before.HandlerMethod == after.HandlerMethod,
            Where($"handler method {before.HandlerMethod} became {after.HandlerMethod}"));

        Assert.True(before.InvokeHandlerType.Name == after.InvokeHandlerType.Name,
            Where($"handler class {before.InvokeHandlerType.Name} became {after.InvokeHandlerType.Name}"));

        Assert.True(
            before.RequestParameterInformationList.Count == after.RequestParameterInformationList.Count,
            Where($"parameter count {before.RequestParameterInformationList.Count} became " +
                  $"{after.RequestParameterInformationList.Count}"));

        foreach (var parameter in before.RequestParameterInformationList) {
            var rebuilt = after.RequestParameterInformationList
                .SingleOrDefault(candidate => candidate.Name == parameter.Name);

            Assert.True(rebuilt != null, Where($"parameter '{parameter.Name}' did not survive"));

            Assert.True(parameter.BindingType == rebuilt!.BindingType,
                Where($"parameter '{parameter.Name}' bound {parameter.BindingType}, rebuilt as {rebuilt.BindingType}"));

            // The gap that let a real defect through: the corpus compared parameters by C# name
            // and never checked what they bind to, so [FromHeader("X-Trace-Id")] string traceId
            // round-tripped as a header called traceId and the suite was green.
            // Effective name: code-first leaves BindingName empty when it matches the parameter,
            // and the emitter treats the two identically. Comparing them literally would fail on a
            // difference nothing downstream can observe.
            Assert.True(Bound(parameter) == Bound(rebuilt),
                Where($"parameter '{parameter.Name}' binds '{Bound(parameter)}', " +
                      $"rebuilt binding '{Bound(rebuilt)}'"));

            Assert.True(parameter.ParameterType.Name == rebuilt.ParameterType.Name,
                Where($"parameter '{parameter.Name}' typed {parameter.ParameterType.Name}, " +
                      $"rebuilt as {rebuilt.ParameterType.Name}"));
        }

        Assert.True(
            before.ResponseInformation.ToString() == after.ResponseInformation.ToString(),
            Where("response information differs after the round trip"));
    }

    /// <summary>What a parameter actually binds to, empty BindingName meaning its own name.</summary>
    private static string Bound(RequestParameterInformation parameter) =>
        string.IsNullOrEmpty(parameter.BindingName) ? parameter.Name : parameter.BindingName;

    /// <summary>
    /// The handler models the attribute-routed pipeline builds for one corpus application.
    /// </summary>
    private static IReadOnlyList<RequestHandlerModel> CodeFirstModels(string scenario) =>
        RequestGeneratorHarness.HandlerModels(WebPipelineCorpus.Source(scenario));

    public static TheoryData<string> Corpus() {
        var data = new TheoryData<string>();

        foreach (var scenario in WebPipelineCorpus.Scenarios) {
            data.Add(scenario);
        }

        return data;
    }
}
