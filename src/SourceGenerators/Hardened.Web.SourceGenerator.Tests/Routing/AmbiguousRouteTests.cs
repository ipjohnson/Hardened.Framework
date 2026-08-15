using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// Two routes matching the same paths and differing only in what their token accepts.
/// </summary>
/// <remarks>
/// Overloading by type makes which handler you reach depend on the content of a value: a user
/// named <c>12345</c> becomes unreachable, a client cannot reason about which endpoint it hit,
/// caches cannot tell the two apart, and the pair is unrepresentable in OpenAPI - one path, one
/// operation per verb. Landed with constraints rather than after them, because constraints are
/// what make the pair writable.
/// </remarks>
public class AmbiguousRouteTests {
    private const string DiagnosticId = "HRDR001";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(FromBodyAttribute)
    ];

    private static GeneratorResult Generate(
        string controllerBody, IReadOnlyDictionary<string, string>? properties = null) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class TestApplication { }

                    public class UserController {
                    {{controllerBody}}
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors,
            additionalTexts: null,
            buildProperties: properties);

    private static Diagnostic? Reported(
        string controllerBody, IReadOnlyDictionary<string, string>? properties = null) =>
        Generate(controllerBody, properties).GeneratorDiagnostics
            .FirstOrDefault(diagnostic => diagnostic.Id == DiagnosticId);

    private const string ConstrainedPair =
        """
            [Get("/users/{id:int}")]
            public string ById(string id) => id;

            [Get("/users/{name}")]
            public string ByName(string name) => name;
        """;

    [Fact]
    public void AConstrainedRouteBesideAnUnconstrainedOneIsReported() {
        Assert.NotNull(Reported(ConstrainedPair));
    }

    /// <summary>The message names both routes, because either one could be the one to move.</summary>
    [Fact]
    public void TheMessageNamesBothRoutes() {
        var message = Reported(ConstrainedPair)!.GetMessage();

        Assert.Contains("/users/{id:int}", message);
        Assert.Contains("/users/{name}", message);
    }

    /// <summary>
    /// <c>{name}</c> versus <c>{*name}</c> is the same rule: the two differ only in how much of the
    /// path the token takes, so which one answers depends on the value.
    /// </summary>
    [Fact]
    public void ATokenBesideACatchAllIsReported() {
        Assert.NotNull(Reported("""
                [Get("/files/{path}")]
                public string One(string path) => path;

                [Get("/files/{*path}")]
                public string Many(string path) => path;
            """));
    }

    /// <summary>
    /// Error by default. An ambiguous pair still produces a table - one route simply becomes
    /// unreachable for some values - so nothing else would make it visible.
    /// </summary>
    [Fact]
    public void ItIsAnErrorByDefault() {
        Assert.Equal(DiagnosticSeverity.Error, Reported(ConstrainedPair)!.Severity);
    }

    /// <summary>
    /// And <c>&lt;HardenedAmbiguousRoutes&gt;</c> lowers it, for a codebase with one legacy pair.
    /// Prefer warning over none: an override that silences leaves no record that the codebase
    /// drifted, and CI runs TreatWarningsAsErrors, so an opt-in still forces a deliberate decision.
    /// </summary>
    [Fact]
    public void TheProjectCanLowerItToAWarning() {
        var reported = Reported(
            ConstrainedPair,
            new Dictionary<string, string> { ["HardenedAmbiguousRoutes"] = "warning" });

        Assert.Equal(DiagnosticSeverity.Warning, reported!.Severity);
    }

    /// <summary>
    /// Literal versus token is untouched. <c>/users/me</c> beside <c>/users/{id}</c> is an ordinary
    /// thing to write, the literal wins, and a document describes both.
    /// </summary>
    [Fact]
    public void ALiteralBesideATokenIsNotReported() {
        Assert.Null(Reported("""
                [Get("/users/me")]
                public string Me() => "me";

                [Get("/users/{id}")]
                public string ById(string id) => id;
            """));
    }

    /// <summary>
    /// Nor is the same constraint on two routes that differ elsewhere - they match different paths,
    /// so nothing is ambiguous.
    /// </summary>
    [Fact]
    public void TwoRoutesWithDifferentShapesAreNotReported() {
        Assert.Null(Reported("""
                [Get("/users/{id:int}")]
                public string ById(string id) => id;

                [Get("/users/{id:int}/posts")]
                public string Posts(string id) => id;
            """));
    }

    /// <summary>
    /// Nor two routes under different verbs. They never contend for the same request, which is what
    /// the rule is about.
    /// </summary>
    [Fact]
    public void TheSameShapeUnderDifferentVerbsIsNotReported() {
        Assert.Null(Reported("""
                [Get("/users/{id:int}")]
                public string ById(string id) => id;

                [Post("/users/{name}")]
                public string Create(string name) => name;
            """));
    }
}
