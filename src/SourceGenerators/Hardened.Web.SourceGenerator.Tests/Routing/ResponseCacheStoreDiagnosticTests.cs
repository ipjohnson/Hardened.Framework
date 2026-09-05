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
/// <para>
/// One of the five mistakes the 0.19 trial's arm B made deliberately, and one of the five that
/// built clean. The attribute is inert without the store package, so every cached route answered an
/// error at run time - which is where each arm found out.
/// </para>
/// <para>
/// Asked of the application, which is the compilation whose entry point applies a web runtime. The
/// 0.20 trial put the store beside the runtime in the template's host, as the docs say to, and the
/// library that declares the handlers warned anyway - so a library says nothing now, and the host
/// is told what the modules it imports cache. The runtime attribute is matched by name, which is
/// why a stand-in declared in the test source is enough.
/// </para>
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
            [KestrelRuntime]
            {{moduleAttributes}}
            public partial class TestApplication { }

            public class KestrelRuntimeAttribute : System.Attribute { }

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
            [KestrelRuntime]
            public partial class TestApplication { }

            public class KestrelRuntimeAttribute : System.Attribute { }

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

    // ---------------------------------------------------------------- where the question is asked

    private const string Library = """
        using Hardened.Requests.Runtime.Caching;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;
        using Hardened.Web.Runtime.Caching;
        using Hardened.Web.Runtime.DependencyInjection;

        namespace Catalog;

        [HardenedModule]
        [HardenedWebModule]
        public partial class CatalogLibrary { }

        // What DependencyModules would generate for the module, written by hand because the
        // library is compiled without generators.
        public class CatalogLibraryAttribute : System.Attribute { }

        public class CatalogController {
            [Get("/catalog")]
            [CacheResponse<VaryByRoute>(Duration = 60)]
            public string Catalog() => "catalog";

            [Get("/offers")]
            public string Offers() => "offers";
        }

        [CacheResponse<VaryByRoute>(Duration = 60)]
        public class OffersController {
            [Get("/deals")]
            public string Deals() => "deals";

            public string NotARoute() => "helper";
        }
        """;

    private static GeneratorResult Host(string moduleAttributes, bool libraryCarriesTheStore = false) {
        var library = libraryCarriesTheStore
            ? Library.Replace("[HardenedWebModule]", "[HardenedWebModule]\n        [Hardened.Requests.Caching.Memory.HardenedMemoryResponseCache]")
            : Library;

        var (reference, _) = GeneratorTestHarness.CompileLibrary(
            library, libraryCarriesTheStore ? "CatalogWithStore" : "Catalog", Anchors);

        return GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Host.cs"] = $$"""
                    using Catalog;
                    using Hardened.Requests.Caching.Memory;
                    using Hardened.Shared.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    {{moduleAttributes}}
                    [CatalogLibrary]
                    public partial class Application { }

                    public class KestrelRuntimeAttribute : System.Attribute { }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors,
            additionalReferences: [reference]);
    }

    /// <summary>
    /// The template's layout: the handlers in a library, the runtime and the store in a host. The
    /// library's compilation applies no runtime, so it is not the application and says nothing.
    /// </summary>
    [Fact]
    public void ALibraryModuleReportsNothingWhateverItCaches() {
        var result = GeneratorTestHarness.Run(
            """
            using Hardened.Requests.Runtime.Caching;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Caching;
            using Hardened.Web.Runtime.DependencyInjection;

            namespace TestApp;

            [HardenedModule]
            [HardenedWebModule]
            public partial class CatalogLibrary { }

            public class CatalogController {
                [Get("/catalog")]
                [CacheResponse<VaryByRoute>(Duration = 60)]
                public string Catalog() => "catalog";
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

        Assert.Empty(Reported(result));
    }

    /// <summary>
    /// And the host is told, by name, what the library it imports caches - read from the
    /// library's metadata, since the host's compilation holds none of its syntax.
    /// </summary>
    [Fact]
    public void AHostImportingACachingLibraryWithNoStoreIsToldWhichHandlersFail() {
        var diagnostic = Assert.Single(Reported(Host("[KestrelRuntime]")));

        Assert.Contains("CatalogController.Catalog", diagnostic.GetMessage());
        Assert.Contains("OffersController.Deals", diagnostic.GetMessage());
        Assert.DoesNotContain("Offers", diagnostic.GetMessage().Replace("OffersController", ""));
        Assert.DoesNotContain("NotARoute", diagnostic.GetMessage());
    }

    [Fact]
    public void AHostImportingACachingLibraryWithTheStoreReportsNothing() {
        Assert.Empty(Reported(Host("[KestrelRuntime] [HardenedMemoryResponseCache]")));
    }

    /// <summary>
    /// The caching guide's arrangement for the template's layout: the library applies the store,
    /// so the test harness that boots the library sees it. The host imports the library, and with
    /// it the store, so it has nothing to be told.
    /// </summary>
    [Fact]
    public void AHostImportingALibraryThatCarriesTheStoreReportsNothing() {
        Assert.Empty(Reported(Host("[KestrelRuntime]", libraryCarriesTheStore: true)));
    }
}
