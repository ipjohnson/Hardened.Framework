using Hardened.SourceGenerator.Tests.Infrastructure;
using Hardened.SourceGenerator.Web;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// <c>HRDW009</c>, a <c>VaryByQuery</c> key that names no query key the operation binds, from the
/// generator's own assembly.
/// </summary>
/// <remarks>
/// The web and OpenAPI generator suites cover the same rule through the copies of this source they
/// compile. These cover the copy in <c>Hardened.SourceGenerator</c>.
/// </remarks>
public class VaryByQueryDiagnosticsTests
{
    private static string Source(string handlers, string types = "") =>
        $$"""
            using Hardened.Requests.Runtime.Caching;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Caching;

            namespace TestApp;

            public class ProductsController {
            {{handlers}}
            }

            {{types}}
            """;

    private static IReadOnlyList<Diagnostic> Reported(string handlers, string types = "") =>
        RequestGeneratorHarness
            .Generate(Source(handlers, types))
            .GeneratorDiagnostics.Where(diagnostic =>
                diagnostic.Id == VaryByQueryDiagnostics.DiagnosticId
            )
            .ToList();

    [Fact]
    public void AKeyTheOperationDoesNotBindIsReportedWithTheKeysItDoes()
    {
        var diagnostic = Assert.Single(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("category", "cursr")]
                    public string List(
                        [FromQueryString] string category,
                        [FromQueryString("cursor")] string next
                    ) => category + next;
                """
            )
        );

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'cursr'", diagnostic.GetMessage());
        Assert.Contains("'category', 'cursor'", diagnostic.GetMessage());
    }

    [Fact]
    public void KeysTheOperationBindsAreNotReported()
    {
        Assert.Empty(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("category")]
                    public string List([FromQueryString] string category) => category;
                """
            )
        );
    }

    [Fact]
    public void NoKeysIsNotReported()
    {
        Assert.Empty(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>(Duration = 10)]
                    public string List([FromQueryString] string category) => category;
                """
            )
        );
    }

    /// <summary>
    /// A key repeated across two declarations is one report, and the list reads "none" for an
    /// operation that binds nothing from the query string.
    /// </summary>
    [Fact]
    public void ARepeatedKeyIsReportedOnce()
    {
        var models = RequestGeneratorHarness.HandlerModels(
            Source(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("page")]
                    [CacheResponse<VaryByHeader>("X-Tenant")]
                    [CacheResponse<VaryByQuery>("page", Duration = 10)]
                    public string List() => "products";
                """
            )
        );

        Assert.Equal(["page"], VaryByQueryDiagnostics.UnboundKeys(Assert.Single(models)));

        var diagnostic = Assert.Single(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("page")]
                    [CacheResponse<VaryByQuery>("page", Duration = 10)]
                    public string List() => "products";
                """
            )
        );

        Assert.Contains("are none", diagnostic.GetMessage());
    }

    [Fact]
    public void AQueryModelBindsItsMembersByTheirWireNames()
    {
        var models = RequestGeneratorHarness.HandlerModels(
            Source(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("category", "Page")]
                    public string List([FromQueryString] Filter filter) => filter.Category;
                """,
                """
                public class Filter
                {
                    public string Category { get; set; } = "";

                    public int Page { get; set; }
                }
                """
            )
        );

        Assert.Equal(["Page"], VaryByQueryDiagnostics.UnboundKeys(Assert.Single(models)));
    }
}
