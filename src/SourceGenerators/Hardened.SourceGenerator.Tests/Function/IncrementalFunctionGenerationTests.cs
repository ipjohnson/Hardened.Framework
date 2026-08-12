using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// Two consecutive runs over the same driver, asserting what the second did with the first's cached
/// output.
///
/// <para>
/// The function pipeline puts two comparers in the way: <c>RequestHandlerModelComparer</c> on the
/// handler models, and <c>FunctionIncrementalGenerator.CombinedComparer</c> on the entry point
/// paired with the collected handlers. The second is the interesting one — it is the only place the
/// function generator supplies a comparer of its own, and a whole-application view is what an
/// unrelated edit is most likely to churn.
/// </para>
/// </summary>
public class IncrementalFunctionGenerationTests {

    private static string Application(
        string signature = "public void Process(DataModel model) { }",
        string functionAttribute = "[HardenedFunction]",
        string extraMembers = "",
        string applicationMembers = "") => $$"""
        using System;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Requests.Abstract.Execution;
        using Hardened.Shared.Runtime.Attributes;

        namespace TestApp;

        public interface IThing { }

        public class DataModel { public string Value { get; set; } = ""; }

        [HardenedModule]
        public partial class TestApplication {
            {{applicationMembers}}
        }

        public class TestFunctions {
            {{extraMembers}}

            {{functionAttribute}}
            {{signature}}
        }
        """;

    private static IncrementalRunResult Rerun(string first, string second) =>
        GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = first },
            new Dictionary<string, string> { ["Test.cs"] = second },
            [new FunctionGenerator()],
            FunctionGeneratorHarness.Anchors);

    private static void AssertRegenerated(IncrementalRunResult result) {
        Assert.False(result.AllOutputsCached,
            "the edit changes generated code, so serving the cached output would leave the " +
            "generated file describing the previous source");

        Assert.NotEqual(result.FirstRun.Values, result.SecondRun.Values);
    }

    /// <summary>The unchanged compilation. Anything less than fully cached here is a bug.</summary>
    [Fact]
    public void RerunningOverIdenticalSourceReusesEveryOutput() {
        var result = Rerun(Application(), Application());

        Assert.True(result.AllOutputsCached);
        Assert.Equal(result.FirstRun, result.SecondRun);
    }

    /// <summary>
    /// A comment. It changes the syntax tree, so both models are rebuilt and then have to compare
    /// equal to the previous ones — which is the whole reason the comparers exist.
    /// </summary>
    [Fact]
    public void AddingACommentReusesEveryOutput() {
        var result = Rerun(Application(), Application(extraMembers: "// an explanatory comment"));

        Assert.True(result.AllOutputsCached);
        Assert.Equal(result.FirstRun, result.SecondRun);
    }

    /// <summary>
    /// Editing the handler's own body. Only the signature reaches the model, and editing a body is
    /// most of what happens between generator runs in an editor.
    /// </summary>
    [Fact]
    public void ChangingAHandlerBodyReusesEveryOutput() {
        var result = Rerun(
            Application(signature: "public void Process(DataModel model) { }"),
            Application(signature: "public void Process(DataModel model) { var x = model.Value; }"));

        Assert.True(result.AllOutputsCached);
    }

    /// <summary>
    /// A method on the handler class that is not itself a function. It cannot affect any generated
    /// file.
    /// </summary>
    [Fact]
    public void AddingANonHandlerMethodReusesEveryOutput() {
        var result = Rerun(Application(), Application(extraMembers: "private string Helper() => \"x\";"));

        Assert.True(result.AllOutputsCached);
    }

    /// <summary>
    /// A member added to the application class itself. This goes through the entry-point model
    /// rather than the handler model, and the combined comparer has to hold it steady — the
    /// registration does not depend on the application's members.
    /// </summary>
    [Fact]
    public void AddingAMemberToTheApplicationReusesTheRegistration() {
        var result = Rerun(Application(), Application(applicationMembers: "// nothing that matters"));

        Assert.True(result.AllOutputsCached);
    }

    [Fact]
    public void ChangingTheFunctionNameRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Application(),
            Application(functionAttribute: "[HardenedFunction(\"renamed\")]")));
    }

    /// <summary>
    /// Naming a function changes it from the provider's catch-all into a switch case, so the
    /// registration has to be rebuilt as well as the handler.
    /// </summary>
    [Fact]
    public void NamingAFunctionRebuildsTheRegistration() {
        var result = Rerun(
            Application(),
            Application(functionAttribute: "[HardenedFunction(\"renamed\")]"));

        Assert.NotEqual(
            result.FirstRun["TestApplication.FunctionHandlers.cs"],
            result.SecondRun["TestApplication.FunctionHandlers.cs"]);

        Assert.Contains("switch (functionName)", result.SecondRun["TestApplication.FunctionHandlers.cs"]);
    }

    [Fact]
    public void ChangingAParameterTypeRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Application(),
            Application(signature: "public void Process(IThing thing) { }")));
    }

    [Fact]
    public void AddingAParameterRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Application(),
            Application(signature: "public void Process(DataModel model, IThing thing) { }")));
    }

    /// <summary>
    /// Sync to async. The return type changes and so does every constructor the handler is built
    /// with, so a cached result here would be a handler that never awaits.
    /// </summary>
    [Fact]
    public void MakingAHandlerAsyncRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Application(),
            Application(signature: "public Task Process(DataModel model) => Task.CompletedTask;")));
    }

    /// <summary>
    /// A value-returning handler that starts returning nothing. The invoke method stops assigning a
    /// response value, so the emitted text changes as well as the model.
    /// </summary>
    [Fact]
    public void ChangingAHandlerToReturnNothingRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Application(signature: "public string Process(DataModel model) => model.Value;"),
            Application(signature: "public void Process(DataModel model) { }")));
    }

    /// <summary>
    /// Adding a handler produces a new file and leaves the existing one untouched, which is what
    /// keeps a large project's rebuild proportional to the edit. The registration must still be
    /// rebuilt — it is a whole-application view.
    /// </summary>
    [Fact]
    public void AddingAHandlerLeavesTheExistingHandlersOutputUnchanged() {
        var result = Rerun(
            Application(),
            Application(extraMembers: """
                [HardenedFunction("other")]
                public void Other() { }
                """));

        Assert.Equal(result.FirstRun["Process.FunctionHandler.cs"], result.SecondRun["Process.FunctionHandler.cs"]);
        Assert.Contains("other.FunctionHandler.cs", result.SecondRun.Keys);

        Assert.NotEqual(
            result.FirstRun["TestApplication.FunctionHandlers.cs"],
            result.SecondRun["TestApplication.FunctionHandlers.cs"]);
    }

    /// <summary>Removing a handler removes its file rather than leaving it behind.</summary>
    [Fact]
    public void RemovingAHandlerRemovesItsOutput() {
        var result = Rerun(
            Application(extraMembers: """
                [HardenedFunction("other")]
                public void Other() { }
                """),
            Application());

        Assert.Contains("other.FunctionHandler.cs", result.FirstRun.Keys);
        Assert.DoesNotContain("other.FunctionHandler.cs", result.SecondRun.Keys);
    }

    /// <summary>
    /// Renaming the application class moves the registration to a new file, and the old one is not
    /// left behind.
    /// </summary>
    [Fact]
    public void RenamingTheApplicationMovesTheRegistrationFile() {
        var result = Rerun(
            Application(),
            Application().Replace("class TestApplication", "class RenamedApplication"));

        Assert.Contains("TestApplication.FunctionHandlers.cs", result.FirstRun.Keys);
        Assert.Contains("RenamedApplication.FunctionHandlers.cs", result.SecondRun.Keys);
        Assert.DoesNotContain("TestApplication.FunctionHandlers.cs", result.SecondRun.Keys);
    }
}
