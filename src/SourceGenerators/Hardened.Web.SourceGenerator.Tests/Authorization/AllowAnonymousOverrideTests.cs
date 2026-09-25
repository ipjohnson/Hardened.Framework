using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Authorization;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Authorization;

/// <summary>
/// A requirement on a handler's method that <c>[AllowAnonymous]</c> cancels, reported at build.
/// </summary>
public class AllowAnonymousOverrideTests
{
    private const string DiagnosticId = "HAUTH002";

    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute),
        typeof(FromBodyAttribute),
        typeof(AllowAnonymousAttribute),
    ];

    private static IReadOnlyList<Diagnostic> Reported(
        string controllerAttributes,
        string controllerBody,
        string moduleAttributes = ""
    ) =>
        GeneratorTestHarness
            .Run(
                new Dictionary<string, string>
                {
                    ["Test.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;
                    using Hardened.Requests.Runtime.Authorization;

                    namespace TestApp;

                    [HardenedModule]
                    {{moduleAttributes}}
                    public partial class TestApplication { }

                    public class NamedGrantAttribute : AuthorizeGrantsAttribute
                    {
                        public NamedGrantAttribute() : base("orders:write") { }
                    }

                    {{controllerAttributes}}
                    public class OrderController {
                    {{controllerBody}}
                    }
                    """,
                },
                new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
                Anchors
            )
            .GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == DiagnosticId)
            .ToList();

    [Fact]
    public void BothOnTheMethodAreReported()
    {
        var reported = Assert.Single(
            Reported(
                "",
                """
                    [Get("/orders")]
                    [AuthorizeGrants("orders:read")]
                    [AllowAnonymous]
                    public string All() => "";
                """
            )
        );

        Assert.Equal(DiagnosticSeverity.Warning, reported.Severity);
        Assert.Contains("OrderController.All", reported.GetMessage());
    }

    /// <summary>
    /// <c>[AllowAnonymous]</c> on the class cancels a grant written on the method below it.
    /// </summary>
    [Fact]
    public void AnonymousOnTheClassOverAGrantOnTheMethodIsReported()
    {
        Assert.Single(
            Reported(
                "[AllowAnonymous]",
                """
                    [Get("/orders")]
                    [AuthorizeGrants("orders:read")]
                    public string All() => "";
                """
            )
        );
    }

    /// <summary>
    /// The way one route in a guarded class is made public, which is meant and is not reported.
    /// </summary>
    [Fact]
    public void AnonymousOnTheMethodUnderAGrantOnTheClassIsNotReported()
    {
        Assert.Empty(
            Reported(
                "[AuthorizeGrants(\"orders:read\")]",
                """
                    [Get("/orders")]
                    [AllowAnonymous]
                    public string All() => "";
                """
            )
        );
    }

    [Fact]
    public void AGrantAloneIsNotReported()
    {
        Assert.Empty(
            Reported(
                "",
                """
                    [Get("/orders")]
                    [AuthorizeGrants("orders:read")]
                    public string All() => "";
                """
            )
        );
    }

    /// <summary>An application's own attribute is a requirement the same way.</summary>
    [Fact]
    public void AnAttributeOfTheApplicationsOwnIsReported()
    {
        Assert.Single(
            Reported(
                "",
                """
                    [Post("/orders")]
                    [NamedGrant]
                    [AllowAnonymous]
                    public string Create() => "";
                """
            )
        );
    }

    /// <summary>Reported whether or not the application denies by default.</summary>
    [Fact]
    public void ItIsReportedUnderRequireAuthorizationToo()
    {
        Assert.Single(
            Reported(
                "",
                """
                    [Get("/orders")]
                    [AuthorizeGrants("orders:read")]
                    [AllowAnonymous]
                    public string All() => "";
                """,
                "[RequireAuthorization]"
            )
        );
    }
}
