using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Caching;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Web;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.ResponseCaching;

/// <summary>
/// A <c>VaryByQuery</c> key that names no query key the operation binds.
/// </summary>
/// <remarks>
/// The 0.41 trial's catalogue keyed on <c>cursr</c> for <c>cursor</c>, built clean, and served the
/// first page for the second until the entry expired.
/// </remarks>
public class VaryByQueryDiagnosticTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute), // Hardened.Web.Runtime
        typeof(FromBodyAttribute), // Hardened.Requests.Abstract
        typeof(CacheResponseAttribute<>), // Hardened.Requests.Runtime
    ];

    private static IReadOnlyList<Diagnostic> Reported(string handlers, string types = "")
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            using Hardened.Requests.Runtime.Caching;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Caching;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class ProductsController {
            {{handlers}}
            }

            {{types}}
            """,
            new WebLibrarySourceGenerator(),
            Anchors
        );

        return result
            .GeneratorDiagnostics.Where(diagnostic =>
                diagnostic.Id == VaryByQueryDiagnostics.DiagnosticId
            )
            .ToList();
    }

    [Fact]
    public void AMisspeltKeyIsHRDW009()
    {
        var diagnostic = Assert.Single(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("category", "cursr", Duration = 10)]
                    public string List(
                        [FromQueryString] string category,
                        [FromQueryString] string cursor
                    ) => category + cursor;
                """
            )
        );

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);

        var message = diagnostic.GetMessage();

        Assert.Contains("ProductsController.List", message);
        Assert.Contains("'cursr'", message);
        Assert.Contains("'category', 'cursor'", message);
    }

    [Fact]
    public void KeysTheOperationBindsReportNothing()
    {
        Assert.Empty(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("category", "cursor")]
                    public string List(
                        [FromQueryString] string category,
                        [FromQueryString] string cursor
                    ) => category + cursor;
                """
            )
        );
    }

    /// <summary>The wire name, which is what the strategy reads, not the C# name.</summary>
    [Fact]
    public void ABindingNameIsTheKeyToName()
    {
        var diagnostic = Assert.Single(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("pageToken")]
                    public string List([FromQueryString("page_token")] string pageToken) => pageToken;
                """
            )
        );

        Assert.Contains("'page_token'", diagnostic.GetMessage());
    }

    /// <summary>
    /// A model bound from the query string binds its members' keys, by their wire names.
    /// <c>Category</c> is sent as <c>category</c>, so naming the property would be reported too.
    /// </summary>
    [Fact]
    public void AQueryModelsMembersAreBoundKeys()
    {
        var reported = Reported(
            """
                [Get("/products")]
                [CacheResponse<VaryByQuery>("category", "missing")]
                public string List([FromQueryString] Filter filter) => filter.Category;
            """,
            """
            public class Filter
            {
                public string Category { get; set; } = "";

                public int Page { get; set; }
            }
            """
        );

        var message = Assert.Single(reported).GetMessage();

        Assert.Contains("'missing'", message);
        Assert.Contains("'category', 'page'", message);
    }

    /// <summary>
    /// A constant is a constant string, so it is checked the way a literal is. Public, because the
    /// arguments are copied into the handler's generated metadata.
    /// </summary>
    [Fact]
    public void AConstantKeyIsChecked()
    {
        var diagnostic = Assert.Single(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>(QueryKeys.Category, QueryKeys.Cursor)]
                    public string List(
                        [FromQueryString] string category,
                        [FromQueryString] string cursor
                    ) => category + cursor;
                """,
                """
                public static class QueryKeys
                {
                    public const string Category = "category";

                    public const string Cursor = "cursr";
                }
                """
            )
        );

        Assert.Contains("'cursr'", diagnostic.GetMessage());
    }

    [Fact]
    public void NoKeysReportsNothing()
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

    [Fact]
    public void AnOperationBindingNoQueryKeySaysNone()
    {
        var diagnostic = Assert.Single(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByQuery>("page")]
                    public string List() => "products";
                """
            )
        );

        Assert.Contains("are none", diagnostic.GetMessage());
    }

    /// <summary>Another strategy's values are not query keys.</summary>
    [Fact]
    public void AnotherStrategyIsNotChecked()
    {
        Assert.Empty(
            Reported(
                """
                    [Get("/products")]
                    [CacheResponse<VaryByHeader>("X-Tenant")]
                    public string List() => "products";
                """
            )
        );
    }

    /// <summary>A declaration on the class is checked against each handler it reaches.</summary>
    [Fact]
    public void ADeclarationOnTheControllerIsChecked()
    {
        var result = GeneratorTestHarness.Run(
            """
            using Hardened.Requests.Runtime.Caching;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Caching;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            [CacheResponse<VaryByQuery>("category")]
            public class ProductsController {
                [Get("/products")]
                public string List([FromQueryString] string category) => category;

                [Get("/offers")]
                public string Offers() => "offers";
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors
        );

        var diagnostic = Assert.Single(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == VaryByQueryDiagnostics.DiagnosticId
        );

        Assert.Contains("ProductsController.Offers", diagnostic.GetMessage());
    }
}
