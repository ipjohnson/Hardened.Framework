using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Web.Routing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// The request body read where there is none: two parameters that both fell to it, one on a verb
/// that carries no body, and a registered service that fell to it because its constructor was
/// ordinary.
/// </summary>
/// <remarks>
/// Three findings from the 0.20 trial's probe controller. A GET taking the template's own
/// <c>TodoStore</c> built clean, answered 400 on every request and published a request body on a
/// GET; a POST taking a counter and a payload built with a CS7036 in generated code. Nothing named
/// the convention that decided either.
/// </remarks>
public class BodyParameterDiagnosticsTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute), // Hardened.Web.Runtime
        typeof(FromBodyAttribute), // Hardened.Requests.Abstract
        typeof(SingletonServiceAttribute), // DependencyModules.Runtime
    ];

    private static GeneratorResult Generate(string handler) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Test.cs"] = $$"""
                using DependencyModules.Runtime.Attributes;
                using Hardened.Requests.Abstract.Attributes;
                using Hardened.Shared.Runtime.Attributes;
                using Hardened.Web.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                public partial class TestApplication { }

                public interface ICounter { }

                [SingletonService]
                public class Counter : ICounter { }

                [SingletonService]
                public class Audit { }

                [CrossWireService]
                public class Wired : ICounter { }

                [SingletonService]
                public class Closer : System.IDisposable { public void Dispose() { } }

                public record Reading(string Sensor, int Value);

                public class EventController {
                {{handler}}
                }
                """,
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors
        );

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result, string id) =>
        result.GeneratorDiagnostics.Where(reported => reported.Id == id);

    // ---------------------------------------------------------------- HRDR009

    [Fact]
    public void TwoBodyParametersAreAnErrorNamingBoth()
    {
        var result = Generate(
            """
            [Post("/events")]
            public string Handle(Reading first, Reading second) => "";
            """
        );

        var diagnostic = Assert.Single(
            Reported(result, BodyParameterDiagnostics.SeveralBodiesDiagnosticId)
        );

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("EventController.Handle", diagnostic.GetMessage());
        Assert.Contains("'first' and 'second'", diagnostic.GetMessage());
        Assert.Contains("[FromServices]", diagnostic.GetMessage());
    }

    /// <summary>
    /// And it is the only thing reported: the handler is not emitted, so no compiler error
    /// arrives beside it.
    /// </summary>
    /// <remarks>
    /// The generated invocation omitted an argument the method requires, so the build failed on a
    /// <c>CS7036</c> inside <c>obj/</c> as well - a second, worse account of a defect HRDR009
    /// already stated in full, pointing at a file nobody wrote. Skipping is safe for the reason it
    /// is safe for an unresolved parameter: the routing table skips the same handlers, and
    /// HRDR009 is an error, so nothing reaches a running service either way.
    /// </remarks>
    [Fact]
    public void TwoBodyParametersReportNothingBesideHRDR009()
    {
        var result = Generate(
            """
            [Post("/events")]
            public string Handle(Reading first, Reading second) => "";
            """
        );

        var error = Assert.Single(result.Errors).ToString();

        Assert.Contains(BodyParameterDiagnostics.SeveralBodiesDiagnosticId, error);
        Assert.DoesNotContain("CS7036", error);
    }

    /// <summary>
    /// The routing table skips it too. Routing to a handler class that was never written is
    /// uncompilable output, which is the failure the skip exists to prevent.
    /// </summary>
    [Fact]
    public void AHandlerWithTwoBodyParametersIsNotRoutedTo()
    {
        var result = Generate(
            """
            [Post("/events")]
            public string Handle(Reading first, Reading second) => "";

            [Post("/readings")]
            public string One(Reading reading) => "";
            """
        );

        var routing = result
            .GeneratedSources.Single(source =>
                source.Key.Contains("Routing", StringComparison.Ordinal)
            )
            .Value;

        Assert.DoesNotContain("EventController_Handle", routing);
        Assert.Contains("EventController_One", routing);
    }

    [Fact]
    public void OneBodyParameterIsNotReported()
    {
        var result = Generate(
                """
                [Post("/events")]
                public string Handle(Reading reading) => "";
                """
            )
            .AssertNoErrors();

        Assert.Empty(Reported(result, BodyParameterDiagnostics.SeveralBodiesDiagnosticId));
    }

    // ---------------------------------------------------------------- HRDR007, the registered service

    /// <summary>
    /// A parameterless service passes the constructor test HRDR007 was built on, and its
    /// registration attribute is the statement that settles it.
    /// </summary>
    [Theory]
    [InlineData("Get")]
    [InlineData("Post")]
    public void ARegisteredServiceParameterIsHRDR007WhateverTheVerb(string verb)
    {
        var result = Generate(
            $$"""
            [{{verb}}("/events")]
            public string Handle(Counter counter) => "";
            """
        );

        var diagnostic = Assert.Single(Reported(result, ServiceParameterDiagnostics.DiagnosticId));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Counter' is registered as a service", diagnostic.GetMessage());
        Assert.Contains("[SingletonService]", diagnostic.GetMessage());
        Assert.Empty(Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId));
    }

    /// <summary>
    /// The fix it offers has to be one that works. <c>Counter</c> is registered against
    /// <c>ICounter</c> and not against itself, so <c>[FromServices] Counter</c> - which this led
    /// with - builds clean and throws on the first request. It names the interface instead.
    /// </summary>
    [Fact]
    public void TheAdviceForAServiceWithAnInterfaceDoesNotOfferFromServices()
    {
        var message = Assert
            .Single(
                Reported(
                    Generate(
                        """
                        [Get("/events")]
                        public string Handle(Counter counter) => "";
                        """
                    ),
                    ServiceParameterDiagnostics.DiagnosticId
                )
            )
            .GetMessage();

        Assert.Contains("Type 'counter' as 'ICounter'", message);
        Assert.Contains("[CrossWireService]", message);
        Assert.DoesNotContain("[FromServices]", message);
    }

    /// <summary>
    /// And where it does work it is still offered. A service with no interface is registered
    /// against itself, so the container can build it by the type the parameter names.
    /// </summary>
    [Fact]
    public void TheAdviceForAServiceWithNoInterfaceStillOffersFromServices()
    {
        var message = Assert
            .Single(
                Reported(
                    Generate(
                        """
                        [Get("/events")]
                        public string Handle(Audit audit) => "";
                        """
                    ),
                    ServiceParameterDiagnostics.DiagnosticId
                )
            )
            .GetMessage();

        Assert.Contains("[FromServices]", message);
    }

    [Fact]
    public void TheSameServiceAsItsInterfaceReportsNothing()
    {
        var result = Generate(
                """
                [Get("/events")]
                public string Handle(ICounter counter) => "";
                """
            )
            .AssertNoErrors();

        Assert.Empty(Reported(result, ServiceParameterDiagnostics.DiagnosticId));
        Assert.Empty(Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId));
    }

    // ---------------------------------------------------------------- HRDR010

    [Theory]
    [InlineData("Get")]
    [InlineData("Delete")]
    public void ABodyParameterOnABodylessVerbIsAWarning(string verb)
    {
        var result = Generate(
            $$"""
            [{{verb}}("/events")]
            public string Handle(Reading reading) => "";
            """
        );

        var reported = Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId).ToList();

        if (verb == "Delete")
        {
            // DELETE is not bodyless: HTTP permits a body on one and some APIs send it.
            Assert.Empty(reported);

            return;
        }

        var diagnostic = Assert.Single(reported);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'reading'", diagnostic.GetMessage());
        Assert.Contains("a GET carries none", diagnostic.GetMessage());
        Assert.Contains("HRDR010", diagnostic.GetMessage());
    }

    /// <summary>A parameter a route token displaced is HRDR005's, which says why it moved.</summary>
    [Fact]
    public void AParameterHRDR005ReportsIsLeftToIt()
    {
        var result = Generate(
            """
            [Get("/events/{eventid}")]
            public string Handle(string eventId) => eventId;
            """
        );

        Assert.Single(Reported(result, RouteBindingDiagnostics.DiagnosticId));
        Assert.Empty(Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId));
    }

    [Fact]
    public void ABodyOnAPostIsNotReported()
    {
        var result = Generate(
                """
                [Post("/events")]
                public string Handle(Reading reading) => "";
                """
            )
            .AssertNoErrors();

        Assert.Empty(Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId));
    }

    // ------------------------------------------------- HRDR015, the service nothing resolves

    /// <summary>
    /// The 500 the advice above used to lead people into. <c>[SingletonService]</c> registers
    /// <c>Counter</c> against <c>ICounter</c> and not against itself, so asking the container for
    /// a <c>Counter</c> compiles, publishes nothing unusual, and throws inside the generated binder
    /// on the first request.
    /// </summary>
    [Fact]
    public void AServiceAskedForByItsOwnTypeIsHRDR015()
    {
        var diagnostic = Assert.Single(
            Reported(
                Generate(
                    """
                    [Get("/events")]
                    public string Handle([FromServices] Counter counter) => "";
                    """
                ),
                UnresolvableServiceDiagnostics.DiagnosticId
            )
        );

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'ICounter'", diagnostic.GetMessage());
        Assert.Contains("[CrossWireService]", diagnostic.GetMessage());
    }

    /// <summary>
    /// A service with no interface is registered against itself, so the parameter resolves.
    /// </summary>
    [Fact]
    public void AServiceWithNoInterfaceIsNotReported()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/events")]
                    public string Handle([FromServices] Audit audit) => "";
                    """
                ),
                UnresolvableServiceDiagnostics.DiagnosticId
            )
        );
    }

    /// <summary>
    /// <c>[CrossWireService]</c> registers the class and points the interfaces at that
    /// registration, which is the fix the other two diagnostics name.
    /// </summary>
    [Fact]
    public void ACrossWiredServiceIsNotReported()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/events")]
                    public string Handle([FromServices] Wired wired) => "";
                    """
                ),
                UnresolvableServiceDiagnostics.DiagnosticId
            )
        );
    }

    /// <summary>
    /// The interface is what the container was asked for, so there is nothing to report.
    /// </summary>
    [Fact]
    public void AnInterfaceParameterIsNotReported()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/events")]
                    public string Handle(ICounter counter) => "";
                    """
                ),
                UnresolvableServiceDiagnostics.DiagnosticId
            )
        );
    }

    /// <summary>
    /// A class carrying no registration attribute is not a service, and asking the container for
    /// one is an ordinary thing to do against a registration written by hand.
    /// </summary>
    [Fact]
    public void AnUnregisteredClassIsNotReported()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/events")]
                    public string Handle([FromServices] Reading reading) => "";
                    """
                ),
                UnresolvableServiceDiagnostics.DiagnosticId
            )
        );
    }

    /// <summary>
    /// Only interfaces outside <c>System</c> count, because those are the ones DependencyModules
    /// registers a class against. It passes over <c>IDisposable</c> as a capability rather than a
    /// role, so a service declaring one is registered against itself and resolves.
    /// </summary>
    [Fact]
    public void AServiceWhoseOnlyInterfaceIsACapabilityIsNotReported()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/events")]
                    public string Handle([FromServices] Closer closer) => "";
                    """
                ),
                UnresolvableServiceDiagnostics.DiagnosticId
            )
        );
    }
}
