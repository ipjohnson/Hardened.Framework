using Hardened.Library.SourceGenerator;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// A module with handlers and nothing compiling them into a routing table.
/// </summary>
/// <remarks>
/// <para>
/// CS-10. Removing <c>Hardened.Web.SourceGenerator</c> built without a warning into an application
/// that answered 404 to every route it declared. The template's AGENTS.md documented the trap and
/// the build said nothing, which is the worst of both: written down where you look after you have
/// already lost the afternoon.
/// </para>
/// <para>
/// The build cannot see a missing analyzer from inside the analyzer that is missing, so the
/// question is asked from one that is still there. <c>Hardened.Library.SourceGenerator</c> is
/// referenced by every Hardened project and by the template, and the routing generators declare a
/// marker type saying they ran - post-initialization output being the one kind of generated source
/// another generator can see.
/// </para>
/// <para>
/// The diagnostic is named by its id rather than through <c>RoutingGeneratorPresence</c>: this test
/// assembly compiles the shared source that type lives in, so the constant is ambiguous across the
/// three generator assemblies referenced here.
/// </para>
/// </remarks>
public class MissingRoutingGeneratorTests {
    private const string DiagnosticId = "HRDR006";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),       // Hardened.Web.Runtime
        typeof(FromBodyAttribute)   // Hardened.Requests.Abstract
    ];

    private const string WithRoutes = """
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        [HardenedWebModule]
        public partial class TestApplication { }

        public class ItemController {
            [Get("/items/{id}")]
            public string Item(string id) => id;
        }
        """;

    private const string WithNoRoutes = """
        using Hardened.Shared.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class ItemService {
            public string Item(string id) => id;
        }
        """;

    private static GeneratorResult Generate(string source, params IIncrementalGenerator[] generators) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source }, generators, Anchors);

    private static bool Reported(GeneratorResult result) =>
        result.GeneratorDiagnostics.Any(diagnostic => diagnostic.Id == DiagnosticId);

    /// <summary>The repro: routes declared, and only the library generator running.</summary>
    [Fact]
    public void RoutesWithNoRoutingGeneratorAreAnError() {
        var result = Generate(WithRoutes, new LibrarySourceGenerator());

        var diagnostic = Assert.Single(
            result.GeneratorDiagnostics.Where(reported => reported.Id == DiagnosticId));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// The message names the type that declared them, what it costs, and both ways out - the
    /// second of which is the one an assembly that never meant to serve them wants.
    /// </summary>
    [Fact]
    public void TheMessageNamesTheTypeAndTheGenerator() {
        var message = Generate(WithRoutes, new LibrarySourceGenerator()).GeneratorDiagnostics
            .Single(reported => reported.Id == DiagnosticId).GetMessage();

        Assert.Contains("TestApp.ItemController", message);
        Assert.Contains("404", message);
        Assert.Contains("Hardened.Web.SourceGenerator", message);
    }

    /// <summary>With the routing generator beside it, nothing is reported.</summary>
    [Fact]
    public void RoutesWithTheRoutingGeneratorAreNotReported() {
        Assert.False(Reported(
            Generate(WithRoutes, new LibrarySourceGenerator(), new WebLibrarySourceGenerator())));
    }

    /// <summary>
    /// Order does not matter. The marker is post-initialization output, which every generator sees
    /// whichever ran first.
    /// </summary>
    [Fact]
    public void TheOrderTheGeneratorsRunInDoesNotMatter() {
        Assert.False(Reported(
            Generate(WithRoutes, new WebLibrarySourceGenerator(), new LibrarySourceGenerator())));
    }

    /// <summary>
    /// An assembly that declares no routes needs no routing generator - which is what the
    /// framework's own web runtime assemblies are, and the reason the check is not on
    /// <c>[HardenedWebModule]</c>: <c>AspNetCoreRuntime</c> declares that attribute to bring the
    /// pipeline and carries no routes at all.
    /// </summary>
    [Fact]
    public void AnAssemblyWithNoRoutesIsNotReported() {
        Assert.False(Reported(Generate(WithNoRoutes, new LibrarySourceGenerator())));
    }

    /// <summary>
    /// One report for an assembly, not one per route. There is a single thing to fix.
    /// </summary>
    [Fact]
    public void EveryRouteInTheAssemblyProducesOneReport() {
        var result = Generate("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class ItemController {
                [Get("/items/{id}")]
                public string Item(string id) => id;

                [Post("/items")]
                public string Create(string body) => body;
            }

            public class PartController {
                [Delete("/parts/{id}")]
                public string Remove(string id) => id;
            }
            """, new LibrarySourceGenerator());

        Assert.Single(result.GeneratorDiagnostics.Where(reported => reported.Id == DiagnosticId));
    }
}
