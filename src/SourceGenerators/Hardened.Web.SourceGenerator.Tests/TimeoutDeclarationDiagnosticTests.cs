using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Filters;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Web;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// <c>[Timeout]</c> declaring no budget.
/// </summary>
/// <remarks>
/// The runtime refuses a zero budget as the handler's chain is composed, which is the first
/// request, answered 500 - and the document export ran ahead of it, publishing
/// <c>x-hardened-timeout: 0</c> from the 0.20 trial's probe. <c>HRDW006</c> is the build saying
/// so.
/// </remarks>
public class TimeoutDeclarationDiagnosticTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),       // Hardened.Web.Runtime
        typeof(FromBodyAttribute),  // Hardened.Requests.Abstract
        typeof(TimeoutAttribute)    // Hardened.Requests.Runtime
    ];

    private static GeneratorResult Generate(string classAttributes, string handlerAttributes) =>
        GeneratorTestHarness.Run(
            $$"""
            using Hardened.Requests.Runtime.Filters;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            {{classAttributes}}
            public class RatesController {
                [Get("/rates")]
                {{handlerAttributes}}
                public string Read() => "rates";
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result) =>
        result.GeneratorDiagnostics.Where(d => d.Id == TimeoutDeclarationDiagnostics.DiagnosticId);

    [Theory]
    [InlineData("[Timeout(Milliseconds = 0)]")]
    [InlineData("[Timeout(Milliseconds = -5)]")]
    public void AZeroOrNegativeBudgetOnTheOperationIsHRDW006(string attribute) {
        var diagnostic = Assert.Single(Reported(Generate("", attribute)));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("RatesController.Read", diagnostic.GetMessage());
        Assert.Contains("greater than zero", diagnostic.GetMessage());
    }

    [Fact]
    public void AZeroBudgetOnTheClassIsReportedForItsOperations() {
        Assert.Single(Reported(Generate("[Timeout(Milliseconds = 0)]", "")));
    }

    [Fact]
    public void ABudgetReportsNothing() {
        var result = Generate("", "[Timeout(Milliseconds = 2000)]").AssertNoErrors();

        Assert.Empty(Reported(result));
    }

    [Fact]
    public void NoDeclarationReportsNothing() {
        var result = Generate("", "").AssertNoErrors();

        Assert.Empty(Reported(result));
    }
}
