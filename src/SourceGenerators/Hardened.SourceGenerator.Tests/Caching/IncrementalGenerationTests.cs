using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Caching;

/// <summary>
/// Two consecutive generator runs over the same driver, asserting what the second one did with the
/// first one's cached output.
///
/// <para>
/// ModelEqualityCachingTests asserts the models compare as they should. These assert the property
/// that actually matters: that Roslyn <em>uses</em> that comparison. A model that compares equal but
/// is never consulted, or a comparer wired to the wrong provider, looks identical in a unit test and
/// makes the IDE regenerate on every keystroke. The opposite failure is worse — a real edit served
/// from cache means the generated code no longer matches the source.
/// </para>
/// </summary>
public class IncrementalGenerationTests {

    private static string Controller(
        string classAttributes = "",
        string methodAttributes = "",
        string path = "/orders/{id}",
        string signature = "public string GetOrder(string id) => id;",
        string extraMembers = "") => $$"""
        using System.Threading.Tasks;
        using Hardened.Requests.Runtime.Filters;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        {{classAttributes}}
        public class OrderController {
            {{extraMembers}}

            [Get("{{path}}")]
            {{methodAttributes}}
            {{signature}}
        }
        """;

    private static IncrementalRunResult Rerun(string first, string second) =>
        GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = first },
            new Dictionary<string, string> { ["Test.cs"] = second },
            [new RequestGenerator()],
            RequestGeneratorHarness.Anchors);

    private static void AssertRegenerated(IncrementalRunResult result) {
        Assert.False(result.AllOutputsCached,
            "the edit changes generated code, so serving the cached output would leave the " +
            "generated file describing the previous source");

        Assert.NotEqual(result.FirstRun.Values, result.SecondRun.Values);
    }

    /// <summary>The unchanged compilation. Anything less than fully cached here is a bug.</summary>
    [Fact]
    public void RerunningOverIdenticalSourceReusesEveryOutput() {
        var result = Rerun(Controller(), Controller());

        Assert.True(result.AllOutputsCached);
        Assert.Equal(result.FirstRun, result.SecondRun);
    }

    /// <summary>
    /// A comment. It changes the syntax tree, so the model is rebuilt — and then has to compare
    /// equal to the previous one, which is the whole reason RequestHandlerModelComparer exists.
    /// </summary>
    [Fact]
    public void AddingACommentReusesEveryOutput() {
        var result = Rerun(Controller(), Controller(extraMembers: "// an explanatory comment"));

        Assert.True(result.AllOutputsCached);
        Assert.Equal(result.FirstRun, result.SecondRun);
    }

    /// <summary>
    /// A method on the controller that is not a handler. It cannot affect any generated file, and
    /// editing helper methods is most of what happens between generator runs in an IDE.
    /// </summary>
    [Fact]
    public void AddingANonHandlerMethodReusesEveryOutput() {
        var result = Rerun(Controller(), Controller(extraMembers: "private string Helper() => \"x\";"));

        Assert.True(result.AllOutputsCached);
    }

    /// <summary>Editing the handler's own body. Only the signature reaches the model.</summary>
    [Fact]
    public void ChangingAHandlerBodyReusesEveryOutput() {
        var result = Rerun(
            Controller(signature: "public string GetOrder(string id) => id;"),
            Controller(signature: "public string GetOrder(string id) => id.Trim();"));

        Assert.True(result.AllOutputsCached);
    }

    [Fact]
    public void ChangingTheRoutePathRegeneratesTheHandler() {
        AssertRegenerated(Rerun(Controller(), Controller(path: "/invoices/{id}")));
    }

    [Fact]
    public void ChangingTheVerbRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Controller(),
            Controller().Replace("[Get(", "[Post(")));
    }

    [Fact]
    public void RenamingAPathTokenRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Controller(),
            Controller(path: "/orders/{orderId}", signature: "public string GetOrder(string orderId) => orderId;")));
    }

    [Fact]
    public void ChangingAParameterTypeRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Controller(),
            Controller(signature: "public string GetOrder(int id) => id.ToString();")));
    }

    /// <summary>
    /// A value-returning handler that starts returning nothing. The generated invoke method stops
    /// assigning a response value, so the emitted text changes as well as the model.
    /// </summary>
    [Fact]
    public void ChangingAHandlerToReturnNothingRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Controller(),
            Controller(signature: "public void GetOrder(string id) { }")));
    }

    /// <summary>
    /// Swapping one value return type for another. The emitted text is unchanged — the invoke
    /// method assigns whatever the handler returns and never names the type — but the model must
    /// still be recomputed rather than served from cache, or a later change that <em>does</em>
    /// depend on the return type would be invisible.
    /// </summary>
    [Fact]
    public void ChangingOneValueReturnTypeForAnotherInvalidatesTheCachedModel() {
        var result = Rerun(
            Controller(),
            Controller(signature: "public int GetOrder(string id) => id.Length;"));

        Assert.False(result.AllOutputsCached);
    }

    /// <summary>
    /// Sync to async. The return type name changes and so does every constructor the handler is
    /// built with, so a cached result here would be a handler that never awaits.
    /// </summary>
    [Fact]
    public void MakingAHandlerAsyncRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Controller(),
            Controller(signature: "public Task<string> GetOrder(string id) => Task.FromResult(id);")));
    }

    [Fact]
    public void AddingAFilterToTheMethodRegeneratesTheHandler() {
        AssertRegenerated(Rerun(Controller(), Controller(methodAttributes: "[Retry(Retries = 2)]")));
    }

    /// <summary>
    /// A filter on the controller reaches every handler it declares, so it has to invalidate them
    /// even though the handler's own declaration is untouched.
    /// </summary>
    [Fact]
    public void AddingAFilterToTheControllerRegeneratesItsHandlers() {
        AssertRegenerated(Rerun(Controller(), Controller(classAttributes: "[Retry(Retries = 2)]")));
    }

    /// <summary>
    /// Changing only a filter's argument. The filter type is the same, so a comparer that compared
    /// types alone would cache this and emit the old retry count.
    /// </summary>
    [Fact]
    public void ChangingAFilterArgumentRegeneratesTheHandler() {
        AssertRegenerated(Rerun(
            Controller(methodAttributes: "[Retry(Retries = 2)]"),
            Controller(methodAttributes: "[Retry(Retries = 5)]")));
    }

    /// <summary>
    /// Adding a handler produces a new file. The existing handler's file is untouched, which is
    /// what keeps a large project's rebuild proportional to the edit.
    /// </summary>
    [Fact]
    public void AddingAHandlerLeavesTheExistingHandlersOutputUnchanged() {
        var result = Rerun(
            Controller(),
            Controller(extraMembers: """
                [Get("/orders")]
                public string All() => "x";
                """));

        var existing = result.FirstRun.Single().Key;

        Assert.Equal(2, result.SecondRun.Count);
        Assert.Equal(result.FirstRun[existing], result.SecondRun[existing]);
    }

    /// <summary>Removing a handler removes its file rather than leaving it behind.</summary>
    [Fact]
    public void RemovingAHandlerRemovesItsOutput() {
        var result = Rerun(
            Controller(extraMembers: """
                [Get("/orders")]
                public string All() => "x";
                """),
            Controller());

        Assert.Equal(2, result.FirstRun.Count);
        Assert.Single(result.SecondRun);
    }

    /// <summary>
    /// The routing table depends on the application module and on every handler, so it is the one
    /// output an unrelated edit is most likely to churn. It must still cache.
    /// </summary>
    [Fact]
    public void AnUnrelatedEditReusesTheRoutingTable() {
        const string application = """
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class Application { }

            public class OrderController {
            """;

        var result = Rerun(
            application + """
                [Get("/orders")]
                public string All() => "x";
            }
            """,
            application + """
                // an explanatory comment
                [Get("/orders")]
                public string All() => "x";
            }
            """);

        Assert.True(result.AllOutputsCached);
        Assert.Contains("Application.Routing.cs", result.SecondRun.Keys);
    }

    /// <summary>
    /// Adding a route rebuilds the routing table — it is a whole-application view, so unlike a
    /// handler file it cannot be left alone.
    /// </summary>
    [Fact]
    public void AddingARouteRebuildsTheRoutingTable() {
        const string application = """
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class Application { }

            public class OrderController {
            """;

        var result = Rerun(
            application + """
                [Get("/orders")]
                public string All() => "x";
            }
            """,
            application + """
                [Get("/orders")]
                public string All() => "x";

                [Get("/orders/{id}")]
                public string One(string id) => id;
            }
            """);

        Assert.False(result.AllOutputsCached);
        Assert.NotEqual(
            result.FirstRun["Application.Routing.cs"],
            result.SecondRun["Application.Routing.cs"]);
    }
}
