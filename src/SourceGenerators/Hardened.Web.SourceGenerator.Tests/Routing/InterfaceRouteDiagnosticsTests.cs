using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// A verb attribute written somewhere a handler cannot be called from.
///
/// <para>
/// <c>[Get]</c> on an interface member threw out of the syntax transform, which Roslyn reports as
/// <c>CS8785</c> and which costs the whole assembly its generated code - every route in the
/// project, not just the declaration that caused it. The same crash reached a record, because the
/// controller was read as a <c>ClassDeclarationSyntax</c> and a record is not one.
/// </para>
/// </summary>
public class InterfaceRouteDiagnosticsTests {
    private const string DiagnosticId = "HRDR013";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(FromBodyAttribute)
    ];

    private static GeneratorResult Generate(
        string declarations, string? second = null) {
        var sources = new Dictionary<string, string> {
            ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;
                    using System.Threading.Tasks;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class TestApplication { }

                    {{declarations}}

                    public class PingController {
                        [Get("/ping")]
                        public string Ping() => "ok";
                    }
                    """
        };

        if (second != null) {
            sources["Second.cs"] = second;
        }

        return GeneratorTestHarness.Run(
            sources,
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);
    }

    /// <summary>
    /// The whole point of the fix. One declaration in the wrong place used to cost every route in
    /// the assembly, so the healthy controller beside it is what this asserts on.
    /// </summary>
    [Fact]
    public void AnInterfaceDeclarationCostsNoOtherRoute() {
        var result = Generate(
            """
            public interface IPetApi {
                [Get("/pets/{id}")]
                Task<string> GetPet(int id);
            }
            """);

        Assert.Empty(result.GeneratorExceptions);
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains("\"/ping\"", result.SourceContaining("Ping"));
    }

    /// <summary>
    /// Skipped, but not silently. A verb attribute is a route declaration, and one that compiles to
    /// nothing reads in review as a route the application serves.
    /// </summary>
    [Fact]
    public void AnInterfaceDeclarationIsReported() {
        var result = Generate(
            """
            public interface IPetApi {
                [Get("/pets/{id}")]
                Task<string> GetPet(int id);
            }
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == DiagnosticId);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("IPetApi.GetPet", diagnostic.GetMessage());
    }

    /// <summary>
    /// Another library's verb attribute of the same name is not a Hardened route declaration, and
    /// reporting it would warn on every Refit client interface that shares a project with a test
    /// host. The selector cannot tell the two apart - it matches the bare name - so the diagnostic
    /// resolves the attribute before it speaks.
    /// </summary>
    [Fact]
    public void AVerbAttributeFromAnotherLibraryIsNotReported() {
        // A namespace of its own, declaring a GetAttribute of its own. A nearer namespace wins
        // over a using, so the [Get] below is that one and never Hardened's - which is exactly how
        // a Refit interface reads in a project that also has Hardened's attributes in scope.
        var result = Generate("", """
            using Hardened.Web.Runtime.Attributes;
            using System.Threading.Tasks;

            namespace Contracts;

            public class GetAttribute(string path) : System.Attribute {
                public string Path { get; } = path;
            }

            public interface IPetApi {
                [Get("/pets/{id}")]
                Task<string> GetPet(int id);
            }
            """);

        Assert.Empty(result.GeneratorExceptions);
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == DiagnosticId);
        Assert.Contains("\"/ping\"", result.SourceContaining("Ping"));
    }

    /// <summary>
    /// A record can be constructed and called, so its handlers are routes like any other. It
    /// crashed for the same reason the interface did and is fixed by the same widening.
    /// </summary>
    [Fact]
    public void ARecordControllerRoutes() {
        var result = Generate(
            """
            public record PetController {
                [Get("/pets")]
                public string List() => "";
            }
            """);

        result.AssertNoErrors();

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == DiagnosticId);
        Assert.Contains("\"/pets\"", result.SourceContaining("List"));
    }
}
