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
public class BodyParameterDiagnosticsTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),              // Hardened.Web.Runtime
        typeof(FromBodyAttribute),         // Hardened.Requests.Abstract
        typeof(SingletonServiceAttribute)  // DependencyModules.Runtime
    ];

    private static GeneratorResult Generate(string handler) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
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

                    public record Reading(string Sensor, int Value);

                    public class EventController {
                    {{handler}}
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result, string id) =>
        result.GeneratorDiagnostics.Where(reported => reported.Id == id);

    // ---------------------------------------------------------------- HRDR009

    [Fact]
    public void TwoBodyParametersAreAnErrorNamingBoth() {
        var result = Generate("""
            [Post("/events")]
            public string Handle(Reading first, Reading second) => "";
            """);

        var diagnostic = Assert.Single(Reported(result, BodyParameterDiagnostics.SeveralBodiesDiagnosticId));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("EventController.Handle", diagnostic.GetMessage());
        Assert.Contains("'first' and 'second'", diagnostic.GetMessage());
        Assert.Contains("[FromServices]", diagnostic.GetMessage());
    }

    [Fact]
    public void OneBodyParameterIsNotReported() {
        var result = Generate("""
            [Post("/events")]
            public string Handle(Reading reading) => "";
            """).AssertNoErrors();

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
    public void ARegisteredServiceParameterIsHRDR007WhateverTheVerb(string verb) {
        var result = Generate($$"""
            [{{verb}}("/events")]
            public string Handle(Counter counter) => "";
            """);

        var diagnostic = Assert.Single(Reported(result, ServiceParameterDiagnostics.DiagnosticId));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Counter' is registered as a service", diagnostic.GetMessage());
        Assert.Contains("[SingletonService]", diagnostic.GetMessage());
        Assert.Contains("[FromServices]", diagnostic.GetMessage());
        Assert.Empty(Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId));
    }

    [Fact]
    public void TheSameServiceAsItsInterfaceReportsNothing() {
        var result = Generate("""
            [Get("/events")]
            public string Handle(ICounter counter) => "";
            """).AssertNoErrors();

        Assert.Empty(Reported(result, ServiceParameterDiagnostics.DiagnosticId));
        Assert.Empty(Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId));
    }

    // ---------------------------------------------------------------- HRDR010

    [Theory]
    [InlineData("Get")]
    [InlineData("Delete")]
    public void ABodyParameterOnABodylessVerbIsAWarning(string verb) {
        var result = Generate($$"""
            [{{verb}}("/events")]
            public string Handle(Reading reading) => "";
            """);

        var reported = Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId).ToList();

        if (verb == "Delete") {
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
    public void AParameterHRDR005ReportsIsLeftToIt() {
        var result = Generate("""
            [Get("/events/{eventid}")]
            public string Handle(string eventId) => eventId;
            """);

        Assert.Single(Reported(result, RouteBindingDiagnostics.DiagnosticId));
        Assert.Empty(Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId));
    }

    [Fact]
    public void ABodyOnAPostIsNotReported() {
        var result = Generate("""
            [Post("/events")]
            public string Handle(Reading reading) => "";
            """).AssertNoErrors();

        Assert.Empty(Reported(result, BodyParameterDiagnostics.BodylessVerbDiagnosticId));
    }
}
