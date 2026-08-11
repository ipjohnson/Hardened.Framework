using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The assertion the suite was missing.
///
/// <para>
/// Before 2026-08-11 every generator test checked <c>driver.GetRunResult().Diagnostics</c> — what
/// the generator <em>reported</em> — and nothing checked whether the C# it <em>wrote</em> compiles.
/// Three defects slipped through as a result: named binding attributes emitted a double-quoted
/// literal, handlers with metadata but no parameters put the metadata array in the parameters slot,
/// and <c>[FromHeader]</c> called <c>Get</c> on a plain dictionary. All three emitted code that did
/// not build, so every project using those features failed to compile. Integration tests caught
/// them, after they shipped.
/// </para>
///
/// <para>
/// Every case here runs through <see cref="GeneratorResult.AssertNoErrors"/>, which compiles the
/// input together with the generated trees.
/// </para>
/// </summary>
public class GeneratedCodeCompilesTests {

    /// <summary>
    /// The assemblies generated web handlers bind against. <c>typeof</c> rather than a name so the
    /// assembly is loaded by the time references are collected.
    /// </summary>
    private static readonly Type[] Anchors = [
        typeof(GetAttribute),          // Hardened.Web.Runtime
        typeof(FromBodyAttribute)      // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string source) =>
        GeneratorTestHarness.Run(source, new WebLibrarySourceGenerator(), Anchors);

    [Fact]
    public void ASimpleRouteCompiles() {
        Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class OrderController {
                [Get("/orders/{id}")]
                public string GetOrder(string id) => id;
            }
            """).AssertNoErrors();
    }

    /// <summary>All five verbs, including the two that were unusable until 2026-08-11.</summary>
    [Theory]
    [InlineData("Get")]
    [InlineData("Post")]
    [InlineData("Put")]
    [InlineData("Delete")]
    [InlineData("Patch")]
    public void EveryVerbCompiles(string verb) {
        Generate($$"""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class ItemController {
                [{{verb}}("/items/{id}")]
                public string Handle(string id) => id;
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// The named form of a binding attribute. This is what emitted a double-quoted string literal
    /// before the generator fix, producing code that could not compile.
    /// </summary>
    [Fact]
    public void NamedQueryStringBindingCompiles() {
        Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class SearchController {
                [Get("/search")]
                public string Search([FromQueryString("q")] string term) => term;
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// <c>[FromHeader]</c> binding. Documented from the framework's first release and, until the
    /// generator fix, incapable of compiling.
    /// </summary>
    [Fact]
    public void HeaderBindingCompiles() {
        Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class TenantController {
                [Get("/tenant")]
                public string Tenant([FromHeader("X-Tenant")] string tenant) => tenant;
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// A handler carrying metadata but taking no parameters — the shape that put the metadata array
    /// in the parameters slot.
    /// </summary>
    [Fact]
    public void MetadataWithNoParametersCompiles() {
        Generate("""
            using Hardened.Requests.Runtime.Filters;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class HealthController {
                [Get("/health")]
                [Retry(Retries = 2)]
                public string Health() => "ok";
            }
            """).AssertNoErrors();
    }

    /// <summary>Every binding source in one signature, the case most likely to break indexing.</summary>
    [Fact]
    public void AllBindingSourcesInOneHandlerCompile() {
        Generate("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record OrderModel(string Sku);

            public class MixedController {
                [Post("/orders/{id}")]
                public string Mixed(
                    string id,
                    [FromQueryString] string filter,
                    [FromHeader("X-Tenant")] string tenant,
                    [FromBody] OrderModel model) => id + filter + tenant + model.Sku;
            }
            """).AssertNoErrors();
    }

    [Fact]
    public void BasePathOnTheClassCompiles() {
        Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [BasePath("/api/orders")]
            public class OrderController {
                [Get("/{id}")]
                public string GetOrder(string id) => id;

                [Delete("/{id}")]
                public string DeleteOrder(string id) => id;
            }
            """).AssertNoErrors();
    }

    [Fact]
    public void AsyncAndTypedReturnsCompile() {
        Generate("""
            using System.Threading.Tasks;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class ShapesController {
                [Get("/void")]
                public void Nothing() { }

                [Get("/task")]
                public Task Task() => System.Threading.Tasks.Task.CompletedTask;

                [Get("/typed")]
                public Task<int> Typed() => System.Threading.Tasks.Task.FromResult(1);

                [Get("/async")]
                public async Task<string> Async() { await System.Threading.Tasks.Task.Yield(); return "x"; }
            }
            """).AssertNoErrors();
    }

    /// <summary>A file with no controllers is not an error, and produces nothing to compile.</summary>
    [Fact]
    public void AFileWithNoControllersProducesNoErrors() {
        Generate("""
            namespace TestApp;

            public class NotAController {
                public string Value => "x";
            }
            """).AssertNoErrors();
    }
}
