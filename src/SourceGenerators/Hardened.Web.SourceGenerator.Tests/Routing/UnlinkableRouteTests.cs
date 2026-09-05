using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// A route HRDR002 refuses has no link, so HRDR002 stands alone.
/// </summary>
/// <remarks>
/// The handler for <c>{id?}</c> is still emitted so the diagnostic is not buried under the CS0246s
/// a missing class would raise - and the links type then declared <c>string id?</c> as a parameter
/// and buried it under a dozen CS1003s instead. Twelve syntax errors ahead of the one line that
/// said what was wrong, in the 0.20 trial.
/// </remarks>
public class UnlinkableRouteTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),       // Hardened.Web.Runtime
        typeof(FromBodyAttribute)   // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string route) =>
        GeneratorTestHarness.Run(
            $$"""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class ItemController {
                [Get("{{route}}")]
                public string Item(string id) => id;

                [Get("/items/{id}/name")]
                public string Name(string id) => id;
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

    [Theory]
    [InlineData("/items/{id?}")]
    [InlineData("/items/{id=5}")]
    [InlineData("/items/{id}/x/{id}")]
    public void ARefusedRouteIsReportedOnceAndCompiles(string route) {
        var result = Generate(route);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "HRDR002");
        Assert.DoesNotContain(result.Errors, diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    /// <summary>The routes beside it keep their links.</summary>
    [Fact]
    public void TheOtherRoutesAreStillLinkable() {
        var links = Generate("/items/{id?}").SourceContaining("TestApplication.Links");

        Assert.Contains("Name(", links);
        Assert.DoesNotContain("id?", links);
    }
}
