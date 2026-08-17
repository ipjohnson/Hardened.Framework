using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Authorization;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Authorization;

/// <summary>
/// A handler that will start refusing requests, reported while there is still somewhere useful to
/// say it.
/// </summary>
/// <remarks>
/// The runtime denies an unannotated handler either way once the application has opted in. The
/// difference this makes is whether that is learned at build or from a 403 on one route in whatever
/// environment somebody happened to exercise it in.
/// </remarks>
public class RequireAuthorizationTests {
    private const string DiagnosticId = "HAUTH001";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(FromBodyAttribute),
        typeof(AllowAnonymousAttribute)
    ];

    private static GeneratorResult Generate(string moduleAttributes, string controllerBody) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;
                    using Hardened.Requests.Runtime.Authorization;

                    namespace TestApp;

                    [HardenedModule]
                    {{moduleAttributes}}
                    public partial class TestApplication { }

                    public class UserController {
                    {{controllerBody}}
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    private static IReadOnlyList<Diagnostic> Reported(string moduleAttributes, string controllerBody) =>
        Generate(moduleAttributes, controllerBody).GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == DiagnosticId)
            .ToList();

    private const string OneUnguardedHandler =
        """
            [Get("/users")]
            public string All() => "";
        """;

    #region not opted in

    /// <summary>
    /// The default posture. Nothing has been said, so nothing is reported and nothing changes for an
    /// application that has adopted none of this.
    /// </summary>
    [Fact]
    public void AnApplicationThatHasNotOptedInReportsNothing() {
        Assert.Empty(Reported("", OneUnguardedHandler));
    }

    #endregion

    #region opted in

    [Fact]
    public void AnUnguardedHandlerIsReported() {
        var reported = Assert.Single(Reported("[RequireAuthorization]", OneUnguardedHandler));

        Assert.Contains("UserController.All", reported.GetMessage());
    }

    /// <summary>
    /// A warning rather than an error, so adopting the attribute does not break a large application
    /// on day one. CI turns warnings into errors, so an unannotated handler still cannot merge.
    /// </summary>
    [Fact]
    public void ItIsAWarningByDefault() {
        var reported = Assert.Single(Reported("[RequireAuthorization]", OneUnguardedHandler));

        Assert.Equal(DiagnosticSeverity.Warning, reported.Severity);
    }

    [Fact]
    public void EveryUnguardedHandlerIsReported() {
        var reported = Reported(
            "[RequireAuthorization]",
            """
                [Get("/users")]
                public string All() => "";

                [Get("/users/{id}")]
                public string ById(string id) => id;
            """);

        Assert.Equal(2, reported.Count);
    }

    #endregion

    #region saying something

    [Fact]
    public void AHandlerWithAGrantAttributeIsNotReported() {
        Assert.Empty(Reported(
            "[RequireAuthorization]",
            """
                [Get("/users")]
                [AuthorizeGrants("users:read")]
                public string All() => "";
            """));
    }

    /// <summary>
    /// Saying a route is public on purpose is saying something. It is the opt-out, so it has to
    /// silence this as surely as a policy does.
    /// </summary>
    [Fact]
    public void AHandlerWithAllowAnonymousIsNotReported() {
        Assert.Empty(Reported(
            "[RequireAuthorization]",
            """
                [Get("/health")]
                [AllowAnonymous]
                public string Health() => "";
            """));
    }

    /// <summary>
    /// A policy written once on a controller counts for every handler in it, because that is how the
    /// pipeline reads it too - a handler's filters carry its controller's attributes.
    /// </summary>
    [Fact]
    public void AControllerLevelAttributeCoversEveryHandlerInIt() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = """
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;
                    using Hardened.Requests.Runtime.Authorization;

                    namespace TestApp;

                    [HardenedModule]
                    [RequireAuthorization]
                    public partial class TestApplication { }

                    [AuthorizeGrants("users:read")]
                    public class UserController {
                        [Get("/users")]
                        public string All() => "";

                        [Get("/users/{id}")]
                        public string ById(string id) => id;
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Id == DiagnosticId));
    }

    /// <summary>
    /// One guarded handler beside one that is not reports only the second, rather than the whole
    /// controller passing because something in it said something.
    /// </summary>
    [Fact]
    public void OnlyTheHandlerThatSaidNothingIsReported() {
        var reported = Assert.Single(Reported(
            "[RequireAuthorization]",
            """
                [Get("/users")]
                [AuthorizeGrants("users:read")]
                public string All() => "";

                [Get("/users/{id}")]
                public string ById(string id) => id;
            """));

        Assert.Contains("UserController.ById", reported.GetMessage());
    }

    #endregion

    #region the runtime half

    /// <summary>
    /// The attribute does two things, and the diagnostic is only one of them. The generator also
    /// emits the registration that makes the runtime deny - which is what covers the handlers a
    /// generator never sees, in a referenced assembly it never compiled.
    /// </summary>
    [Fact]
    public void OptingInEmitsTheRuntimeRegistration() {
        var sources = Generate("[RequireAuthorization]", OneUnguardedHandler).GeneratedSources;

        Assert.Contains(
            sources.Values,
            source => source.Contains(
                "Hardened.Requests.Runtime.Authorization.AuthorizationServiceCollectionExtensions" +
                ".RequireAuthorization("));
    }

    /// <summary>
    /// And an application that has not opted in gets no registration at all, so nothing about its
    /// startup changes.
    /// </summary>
    [Fact]
    public void NotOptingInEmitsNoRegistration() {
        var sources = Generate("", OneUnguardedHandler).GeneratedSources;

        Assert.DoesNotContain(
            sources.Values,
            source => source.Contains("AuthorizationServiceCollectionExtensions.RequireAuthorization("));
    }

    #endregion
}
