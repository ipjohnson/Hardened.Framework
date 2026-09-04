using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Web;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Streaming;

/// <summary>
/// <c>[ServerSentEvents]</c> on a handler with no stream to frame.
///
/// <para>
/// The attribute's own doc promised a build error from the day it shipped, and nothing raised one:
/// the emitter branches on the return type first and ignored the framing, so the author got a
/// buffered JSON response and a document that said so. <c>HRDW004</c> is that error.
/// </para>
/// </summary>
public class ServerSentEventsDiagnosticTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),      // Hardened.Web.Runtime
        typeof(FromBodyAttribute)  // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string handler) =>
        GeneratorTestHarness.Run(
            $$"""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class FeedController {
            {{handler}}
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result) =>
        result.GeneratorDiagnostics.Where(d => d.Id == StreamFramingDiagnostics.DiagnosticId);

    /// <summary>
    /// Every buffered shape: a value, a task, a list, and a synchronous sequence - which is not a
    /// stream either, however much it looks like one.
    /// </summary>
    [Theory]
    [InlineData("public string Latest() => \"one\";")]
    [InlineData("public Task<string> Latest() => Task.FromResult(\"one\");")]
    [InlineData("public List<string> Latest() => new();")]
    [InlineData("public IEnumerable<string> Latest() => new[] { \"one\" };")]
    public void ServerSentEventsOnABufferedHandlerIsHRDW004(string handler) {
        var diagnostic = Assert.Single(Reported(Generate($"""
            [Get("/latest")]
            [ServerSentEvents]
            {handler}
            """)));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("FeedController.Latest", diagnostic.GetMessage());
        Assert.Contains("IAsyncEnumerable<T>", diagnostic.GetMessage());
    }

    [Fact]
    public void ServerSentEventsOnAnAsyncEnumerableHandlerReportsNothing() {
        var result = Generate("""
            [Get("/feed")]
            [ServerSentEvents]
            public async IAsyncEnumerable<string> Feed() {
                yield return "one";
                await Task.CompletedTask;
            }
            """).AssertNoErrors();

        Assert.Empty(Reported(result));
        Assert.Contains("SseFraming.Instance", result.SourceContaining("FeedController_Feed"));
    }

    [Fact]
    public void ABufferedHandlerWithoutTheAttributeReportsNothing() {
        var result = Generate("""
            [Get("/latest")]
            public string Latest() => "one";
            """).AssertNoErrors();

        Assert.Empty(Reported(result));
    }
}
