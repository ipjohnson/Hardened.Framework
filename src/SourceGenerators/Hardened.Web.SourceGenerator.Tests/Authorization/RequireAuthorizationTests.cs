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
    /// An attribute of the application's own, deriving from <c>[AuthorizeGrants]</c>, says as much as
    /// the attribute it derives from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is why the check is by interface rather than by type name. A name list recognises only
    /// the framework's own two attributes, so it reported every handler guarded by a derived one -
    /// a false positive on the diagnostic whose entire purpose is to prevent false negatives, and
    /// one whose obvious fix, <c>[AllowAnonymous]</c>, would genuinely open the route.
    /// </para>
    /// <para>
    /// Naming a grant once and spelling it as a type is the expected way to write authorization by
    /// hand, so warning about it would make the diagnostic something to switch off.
    /// </para>
    /// </remarks>
    [Fact]
    public void AHandlerGuardedByADerivedAttributeIsNotReported() {
        Assert.Empty(Reported(
            "[RequireAuthorization]",
            """
                public sealed class RequiresUserReadAttribute : AuthorizeGrantsAttribute {
                    public RequiresUserReadAttribute() : base("users:read") { }
                }

                [Get("/users")]
                [RequiresUserRead]
                public string All() => "";
            """));
    }

    /// <summary>
    /// So does an attribute implementing <c>IAuthorizeAttribute</c> directly, which the pipeline
    /// honours without it deriving from anything of the framework's.
    /// </summary>
    [Fact]
    public void AHandlerGuardedByACustomAuthorizeAttributeIsNotReported() {
        Assert.Empty(Reported(
            "[RequireAuthorization]",
            """
                public sealed class TenantMemberAttribute
                    : System.Attribute, Hardened.Requests.Abstract.Authorization.IAuthorizeAttribute {
                    public Hardened.Requests.Abstract.Authorization.Requirement Requirement { get; } =
                        Hardened.Requests.Abstract.Authorization.Requirement.Grant("tenant:member");
                }

                [Get("/users")]
                [TenantMember]
                public string All() => "";
            """));
    }

    /// <summary>
    /// And an attribute that merely looks like one is still reported.
    /// </summary>
    /// <remarks>
    /// Matching by interface has to stay exact about what it accepts. Another framework's
    /// <c>[Authorize]</c> imposes nothing the pipeline evaluates, so recognising it would silence the
    /// warning on a handler that is genuinely unguarded - trading a false positive for the false
    /// negative this exists to prevent.
    /// </remarks>
    [Fact]
    public void AHandlerCarryingAnUnrelatedAuthorizeAttributeIsStillReported() {
        Assert.Single(Reported(
            "[RequireAuthorization]",
            """
                public sealed class SomeOtherFrameworksAuthorizeAttribute : System.Attribute { }

                [Get("/users")]
                [SomeOtherFrameworksAuthorize]
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

    #region where it points

    /// <summary>
    /// Reported against the handler's own name, not against nothing.
    /// </summary>
    /// <remarks>
    /// A diagnostic with no location is a line of build output a developer scrolls past. With one,
    /// the build prints a file and a line and an editor can put it where the handler is written -
    /// which for a rule whose whole content is "you forgot to annotate <em>this</em>" is most of its
    /// value.
    /// </remarks>
    [Fact]
    public void TheDiagnosticPointsAtTheHandlerThatSaidNothing() {
        var reported = Assert.Single(Reported("[RequireAuthorization]", OneUnguardedHandler));

        Assert.NotEqual(Location.None, reported.Location);

        // Rebuilt from a path and a span rather than from the tree, so it is an external-file
        // location. It carries the same path, line and column a source location would, which is what
        // the build output and an editor read.
        Assert.Equal(LocationKind.ExternalFile, reported.Location.Kind);

        var span = reported.Location.GetLineSpan();

        Assert.EndsWith("Test.cs", span.Path);

        // The identifier, so an editor underlines the name rather than the whole method.
        Assert.Equal(
            "All".Length,
            span.EndLinePosition.Character - span.StartLinePosition.Character);
    }

    #endregion

    #region caching

    /// <summary>
    /// Reporting this must not cost the generated output its caching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious way to give a diagnostic a location is to put one on the handler model, and it
    /// was measured before being rejected: a comment above twenty handlers took recomputed outputs
    /// from none to 21 of 23, and a genuine route change from 2 to 14. A span is an offset, so every
    /// offset below an edit shifts, and that model is what builds a class per handler, the routing
    /// table and the OpenAPI document.
    /// </para>
    /// <para>
    /// So the location rides on a separate provider that feeds diagnostics and emits nothing. This
    /// pins the property that buys: an edit that changes no generated file still recomputes none of
    /// them, even with the posture on and the diagnostic being reported.
    /// </para>
    /// </remarks>
    [Fact]
    public void ReportingDoesNotCostTheGeneratedOutputItsCaching() {
        string App(string extra) => $$"""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Requests.Runtime.Authorization;

            namespace TestApp;

            [HardenedModule]
            [RequireAuthorization]
            public partial class TestApplication { }

            public class UserController {
                {{extra}}

                [Get("/users")]
                public string All() => "";

                [Get("/users/{id}")]
                public string ById(string id) => id;
            }
            """;

        var result = GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = App("") },
            new Dictionary<string, string> { ["Test.cs"] = App("// a comment") },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

        // Nothing the generator emitted changed.
        Assert.Equal(result.FirstRun, result.SecondRun);

        // And nothing it emitted was rebuilt to find that out: as many outputs were served from
        // cache as there are generated files. The steps that did run are the diagnostic ones, which
        // emit no source.
        var cached = result.OutputReasons.Count(
            reason => reason == IncrementalStepRunReason.Cached);

        Assert.Equal(result.FirstRun.Count, cached);
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
