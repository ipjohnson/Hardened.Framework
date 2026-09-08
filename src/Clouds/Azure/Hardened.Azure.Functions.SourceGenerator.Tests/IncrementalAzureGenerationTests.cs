using Hardened.Azure.Functions.SourceGenerator.Tests.Infrastructure;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.Azure.Functions.SourceGenerator.Tests;

/// <summary>
/// Two consecutive runs over the same driver, asserting what the second did with the first's
/// cached output.
///
/// <para>
/// The Azure pipeline compares four things by value before it writes anything: the entry point
/// model, the collected handlers reduced to their routes, the bound modules read off the build
/// properties, and the assembly name. Each of the last three is wrapped so the pipeline sees a
/// value rather than a fresh array or a fresh options provider - which is what an unrelated edit
/// would otherwise churn on every keystroke.
/// </para>
/// </summary>
public class IncrementalAzureGenerationTests {

    private static string Application(string queue = "orders", string extraMembers = "") => $$"""
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Functions.Runtime.Attributes;

        namespace TestApp;

        public class Order { public string Id { get; set; } = ""; }

        [HardenedModule]
        public partial class TestApplication { }

        public class Handlers {
            {{extraMembers}}

            [Queue("{{queue}}")]
            public void OnOrder(Order order) { }
        }
        """;

    private static IncrementalRunResult Rerun(string first, string second) =>
        GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = first },
            new Dictionary<string, string> { ["Test.cs"] = second },
            [new AzureFunctionsSourceGenerator()],
            AzureGeneratorHarness.Anchors,
            buildProperties: new Dictionary<string, string> {
                ["HardenedQueueModule"] = AzureGeneratorHarness.ServiceBusModule
            });

    /// <summary>The unchanged compilation. Anything less than fully cached here is a bug.</summary>
    [Fact]
    public void RerunningOverIdenticalSourceReusesEveryOutput() {
        var result = Rerun(Application(), Application());

        Assert.True(result.AllOutputsCached);
        Assert.Equal(result.FirstRun, result.SecondRun);
    }

    /// <summary>
    /// A comment changes the syntax tree, so every model is rebuilt and then has to compare equal
    /// to the previous one - which is what the wrappers around the collected handlers and the
    /// bound modules exist for.
    /// </summary>
    [Fact]
    public void AddingACommentReusesEveryOutput() {
        var result = Rerun(Application(), Application(extraMembers: "// nothing the worker cares about"));

        Assert.True(result.AllOutputsCached);
        Assert.Equal(result.FirstRun, result.SecondRun);
    }

    /// <summary>
    /// A member beside the handler that is not a handler. The shim does not depend on it, so the
    /// worker code must not be rewritten for it.
    /// </summary>
    [Fact]
    public void AddingAnUnattributedMethodReusesEveryOutput() {
        var result = Rerun(Application(), Application(extraMembers: "public void Helper() { }"));

        Assert.True(result.AllOutputsCached);
        Assert.Equal(result.FirstRun, result.SecondRun);
    }

    /// <summary>Renaming the queue is a different function, and the output has to say so.</summary>
    [Fact]
    public void RenamingTheQueueRegeneratesTheFunction() {
        var result = Rerun(Application("orders"), Application("returns"));

        Assert.False(result.AllOutputsCached);
        Assert.Contains("Queue_returns", result.SecondRun["TestApplication.AzureFunctions.cs"]);
        Assert.DoesNotContain("Queue_orders", result.SecondRun["TestApplication.AzureFunctions.cs"]);
    }
}
