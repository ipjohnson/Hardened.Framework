using Hardened.Requests.Runtime.Caching;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <c>[CacheResponse]</c> in a described application that registers no store.
/// </summary>
/// <remarks>
/// <para>
/// The attribute-routed table reported this from the day the store shipped; the described table
/// never called the shared report, so a specification-first application was told nothing and found
/// out from a request. Both tables report it now, from the same code, which is why these read like
/// the attribute-routed tests.
/// </para>
/// <para>
/// The runtime and store attributes are matched by name, which is why stand-ins declared in the
/// test source are enough - this project references neither host nor the store package.
/// </para>
/// </remarks>
public class ResponseCacheStoreDiagnosticTests {

    private const string Spec =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              responses:
                '200': { description: ok }
        """;

    private const string CachingImplementation =
        """
        [Handler]
        public class ThingServiceImpl : IThingService {
            [CacheResponse<VaryByRoute>(Duration = 60)]
            public Task ListThings() => Task.CompletedTask;
        }
        """;

    private static string Host(string moduleAttributes, string implementation = "") =>
        $$"""
          using System.Threading.Tasks;
          using Hardened.Requests.Abstract.Attributes;
          using Hardened.Requests.Runtime.Caching;
          using Hardened.Shared.Runtime.Attributes;
          using Hardened.Web.Runtime.Caching;
          using TestNamespace.Services;

          namespace TestNamespace;

          [HardenedModule]
          {{moduleAttributes}}
          public partial class TestApp {
          }

          public class KestrelRuntimeAttribute : System.Attribute { }

          public class HardenedMemoryResponseCacheAttribute : System.Attribute { }

          {{implementation}}
          """;

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result) =>
        result.GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == "HRDW005");

    // ---------------------------------------------------------------- the implementation caches

    [Fact]
    public void ADescribedOperationCachedByItsImplementationWithNoStoreIsHRDW005() {
        var diagnostic = Assert.Single(Reported(
            OpenApiGenerator.Run(Spec, Host("[KestrelRuntime]", CachingImplementation))));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("ListThings", diagnostic.GetMessage());
        Assert.Contains("[HardenedMemoryResponseCache]", diagnostic.GetMessage());
    }

    [Fact]
    public void ADescribedOperationCachedWithTheStoreReportsNothing() {
        Assert.Empty(Reported(OpenApiGenerator.Run(
            Spec, Host("[KestrelRuntime] [HardenedMemoryResponseCache]", CachingImplementation))));
    }

    /// <summary>A described library, hosted by some other compilation, is not asked.</summary>
    [Fact]
    public void ADescribedLibraryModuleReportsNothing() {
        Assert.Empty(Reported(OpenApiGenerator.Run(Spec, Host("", CachingImplementation))));
    }

    [Fact]
    public void ADescribedOperationThatDoesNotCacheReportsNothing() {
        Assert.Empty(Reported(OpenApiGenerator.Run(Spec, Host("[KestrelRuntime]"))));
    }

    // ---------------------------------------------------------------- an imported module caches

    /// <summary>
    /// An attribute-routed library beside the described application, compiled without generators:
    /// the module attribute DependencyModules would write is written by hand, and so is the store
    /// attribute for the variant that carries one.
    /// </summary>
    private static string Library(bool carriesTheStore) =>
        $$"""
          using Hardened.Requests.Runtime.Caching;
          using Hardened.Web.Runtime.Attributes;
          using Hardened.Web.Runtime.Caching;

          namespace Hardened.Requests.Caching.Memory {
              public class HardenedMemoryResponseCacheAttribute : System.Attribute { }
          }

          namespace Catalog {
              {{(carriesTheStore ? "[Hardened.Requests.Caching.Memory.HardenedMemoryResponseCache]" : "")}}
              public partial class CatalogLibrary { }

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
          }
          """;

    private static GeneratorResult Importing(string moduleAttributes, bool libraryCarriesTheStore = false) {
        var (reference, _) = GeneratorTestHarness.CompileLibrary(
            Library(libraryCarriesTheStore),
            libraryCarriesTheStore ? "SpecCatalogWithStore" : "SpecCatalog",
            [typeof(GetAttribute), typeof(CacheResponseAttribute<>)]);

        return OpenApiGenerator.Run(
            new Dictionary<string, string> { ["petstore.yaml"] = Spec },
            Host(moduleAttributes + " [Catalog.CatalogLibrary]"),
            additionalReferences: [reference]);
    }

    [Fact]
    public void AHostImportingACachingLibraryWithNoStoreIsToldWhichHandlersFail() {
        var diagnostic = Assert.Single(Reported(Importing("[KestrelRuntime]")));

        Assert.Contains("CatalogController.Catalog", diagnostic.GetMessage());
        Assert.Contains("OffersController.Deals", diagnostic.GetMessage());
        Assert.DoesNotContain("NotARoute", diagnostic.GetMessage());
    }

    [Fact]
    public void AHostImportingACachingLibraryWithTheStoreReportsNothing() {
        Assert.Empty(Reported(Importing("[KestrelRuntime] [HardenedMemoryResponseCache]")));
    }

    [Fact]
    public void AHostImportingALibraryThatCarriesTheStoreReportsNothing() {
        Assert.Empty(Reported(Importing("[KestrelRuntime]", libraryCarriesTheStore: true)));
    }
}
