using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Caching.Memory;
using Hardened.Requests.Runtime.Caching;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Web.Routing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// <c>[CacheResponse]</c> in an application that registers no store.
/// </summary>
/// <remarks>
/// One of the five mistakes the 0.19 trial's arm B made deliberately, and one of the five that
/// built clean. The attribute is inert without the store package, so every cached route answered an
/// error at run time - which is where each arm found out.
/// </remarks>
public class ResponseCacheStoreDiagnosticTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),                        // Hardened.Web.Runtime
        typeof(FromBodyAttribute),                   // Hardened.Requests.Abstract
        typeof(CacheResponseAttribute<>),            // Hardened.Requests.Runtime
        typeof(HardenedMemoryResponseCacheAttribute) // Hardened.Requests.Caching.Memory
    ];

    private static GeneratorResult Generate(string moduleAttributes, string handlers) =>
        GeneratorTestHarness.Run(
            $$"""
            using Hardened.Requests.Caching.Memory;
            using Hardened.Requests.Runtime.Caching;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            {{moduleAttributes}}
            public partial class TestApplication { }

            public class CatalogController {
            {{handlers}}
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result) =>
        result.GeneratorDiagnostics.Where(
            diagnostic => diagnostic.Id == ResponseCacheStoreDiagnostics.DiagnosticId);

    private const string CachedHandler = """
            [Get("/catalog")]
            [CacheResponse<VaryByRoute>(Duration = 60)]
            public string Catalog() => "catalog";
        """;

    [Fact]
    public void CachingWithNoStoreIsHRDW005() {
        var diagnostic = Assert.Single(Reported(Generate("", CachedHandler)));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("CatalogController.Catalog", diagnostic.GetMessage());
    }

    /// <summary>
    /// A warning rather than an error, because the check reads module attributes and a store
    /// registered by hand in ConfigureServices is invisible to it.
    /// </summary>
    [Fact]
    public void TheReportIsAWarning() {
        Assert.All(
            Reported(Generate("", CachedHandler)),
            diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
    }

    [Fact]
    public void CachingWithTheStoreModuleReportsNothing() {
        Assert.Empty(Reported(Generate("[HardenedMemoryResponseCache]", CachedHandler)));
    }

    [Fact]
    public void AnApplicationDeclaringNoCachingReportsNothing() {
        Assert.Empty(Reported(Generate("", """
                [Get("/catalog")]
                public string Catalog() => "catalog";
            """)));
    }

    /// <summary>
    /// One report per assembly, naming each handler. The missing store is a single mistake however
    /// many handlers depend on it, and a report each would say the same thing several times.
    /// </summary>
    [Fact]
    public void SeveralCachedHandlersAreOneReportNamingThemAll() {
        var diagnostic = Assert.Single(Reported(Generate("", """
                [Get("/catalog")]
                [CacheResponse<VaryByRoute>(Duration = 60)]
                public string Catalog() => "catalog";

                [Get("/offers")]
                [CacheResponse<VaryByRoute>(Duration = 60)]
                public string Offers() => "offers";
            """)));

        Assert.Contains("CatalogController.Catalog", diagnostic.GetMessage());
        Assert.Contains("CatalogController.Offers", diagnostic.GetMessage());
    }

    /// <summary>The declaration on the class rather than the method reaches the same metadata.</summary>
    [Fact]
    public void ADeclarationOnTheControllerIsReportedToo() {
        var result = GeneratorTestHarness.Run(
            """
            using Hardened.Requests.Runtime.Caching;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            [CacheResponse<VaryByRoute>(Duration = 60)]
            public class CatalogController {
                [Get("/catalog")]
                public string Catalog() => "catalog";
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

        Assert.Single(Reported(result));
    }

    /// <summary>The message says how to fix it, which is the whole point of a build-time report.</summary>
    [Fact]
    public void TheMessageNamesThePackageAndTheModuleAttribute() {
        var message = Assert.Single(Reported(Generate("", CachedHandler))).GetMessage();

        Assert.Contains("Hardened.Requests.Caching.Memory", message);
        Assert.Contains("[HardenedMemoryResponseCache]", message);
    }
}
