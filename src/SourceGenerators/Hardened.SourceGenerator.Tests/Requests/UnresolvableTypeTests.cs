using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// A handler signature naming a type the compiler cannot resolve.
///
/// <para>
/// This used to throw out of the syntax transform. Roslyn reports that as <c>CS8785</c> at warning
/// severity and the generator then contributes <em>nothing to the whole assembly</em> — every
/// route, every parameter bag and the dependency registration, lost over one parameter in one
/// method. A compiling project cannot reach it, but an editor does constantly: a signature is
/// briefly invalid on the way to being valid, mid-rename or before the model class is written, and
/// in that window every generated type in the project disappears.
/// </para>
///
/// <para>
/// The cause was <c>parameter.Type?.GetTypeDefinition(context)!</c> — the <c>?.</c> honest about
/// resolution returning null, the <c>!</c> suppressing the warning that said so, and a dereference
/// a few lines later. Fixed 2026-08-12: the parameter is recorded as
/// <see cref="ParameterBindType.Unresolved"/>, the handler is skipped where source is written, and
/// the reason is reported as <c>HOAG010</c>.
/// </para>
/// </summary>
public class UnresolvableTypeTests {

    /// <summary>One handler that cannot resolve its parameter, beside two that are fine.</summary>
    private const string OneBrokenHandler = """
        [Get("/orders")]
        public string List() => "";

        [Post("/orders")]
        public string Save(NotDeclaredAnywhere model) => "";

        [Get("/orders/{id}")]
        public string Get(string id) => id;
        """;

    private static GeneratorResult Generate() =>
        RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller(OneBrokenHandler));

    [Fact]
    public void TheGeneratorDoesNotThrow() {
        Assert.Empty(Generate().GeneratorExceptions);
    }

    /// <summary>
    /// The behaviour the whole change is for: one unbindable signature costs its own handler and
    /// nothing else.
    /// </summary>
    [Fact]
    public void EveryOtherHandlerInTheAssemblyStillGenerates() {
        var result = Generate();

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("List"));
        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("Get"));
    }

    [Fact]
    public void TheHandlerThatCouldNotBindIsNotGenerated() {
        Assert.DoesNotContain(Generate().GeneratedSources.Keys, key => key.Contains("Save"));
    }

    /// <summary>
    /// The routing table has to agree. Routing to a handler class that was never emitted would
    /// produce a table referencing a type that does not exist — uncompilable output, which is
    /// worse than the missing route.
    /// </summary>
    [Fact]
    public void TheRoutingTableDoesNotRouteToTheHandlerThatWasSkipped() {
        // A routing table is only emitted for an application, so this case supplies its own
        // rather than going through the single-controller helper.
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class TestController {
                [Get("/orders")]
                public string List() => "";

                [Post("/orders")]
                public string Save(NotDeclaredAnywhere model) => "";
            }
            """);

        var routing = result.GeneratedSources
            .Where(pair => pair.Value.Contains("class RoutingTable"))
            .Select(pair => pair.Value)
            .FirstOrDefault();

        Assert.True(routing != null,
            "No routing table was generated. Files: " + string.Join(", ", result.GeneratedSources.Keys));

        Assert.DoesNotContain("Save", routing!);

        // The one that did bind is still routed.
        Assert.Contains("List", routing!);
    }

    [Fact]
    public void TheReasonIsReported() {
        var diagnostic = Assert.Single(
            Generate().GeneratorDiagnostics,
            candidate => candidate.Id == UnresolvedHandler.DiagnosticId);

        var message = diagnostic.GetMessage();

        Assert.Contains("Save", message);
        Assert.Contains("model", message);
    }

    /// <summary>
    /// A warning, not an error. The compiler already reports <c>CS0246</c> for the name itself, so
    /// this says only the part it cannot: that the handler was dropped as a result.
    /// </summary>
    [Fact]
    public void TheReasonIsAWarningRatherThanAnError() {
        var diagnostic = Assert.Single(
            Generate().GeneratorDiagnostics,
            candidate => candidate.Id == UnresolvedHandler.DiagnosticId);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    /// <summary>
    /// It is reported once, not once per handler in the assembly - the per-handler output stage
    /// reports it, and the routing table skips silently.
    /// </summary>
    [Fact]
    public void TheReasonIsReportedOncePerBrokenHandler() {
        var reported = Generate().GeneratorDiagnostics
            .Count(candidate => candidate.Id == UnresolvedHandler.DiagnosticId);

        Assert.Equal(1, reported);
    }
}
