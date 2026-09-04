using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// A route token that binds nothing, beside a parameter that fell onto the request body because of
/// it.
///
/// <para>
/// CS-09. <c>[Get("/{eventid}")]</c> against a parameter called <c>eventId</c> built with zero
/// warnings and answered 400 "input does not contain any JSON tokens" to every request, from a
/// route that matched perfectly. Both lists are in the generator's hand.
/// </para>
/// </summary>
public class RouteBindingDiagnosticsTests {
    private const string DiagnosticId = "HRDR005";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),       // Hardened.Web.Runtime
        typeof(FromBodyAttribute)   // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string verb, string route, string parameters) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Requests.Abstract.Attributes;
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class TestApplication { }

                    public class EventBody {
                        public string Title { get; set; } = "";
                    }

                    public class EventController {
                        [{{verb}}("{{route}}")]
                        public string Handle({{parameters}}) => "";
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    private static Diagnostic Reported(string verb, string route, string parameters) {
        var result = Generate(verb, route, parameters);

        var diagnostic = result.GeneratorDiagnostics
            .SingleOrDefault(reported => reported.Id == DiagnosticId);

        Assert.True(diagnostic != null,
            $"'{verb} {route}' with '{parameters}' reported no {DiagnosticId}. Reported: " +
            string.Join(", ", result.GeneratorDiagnostics.Select(reported => reported.Id)));

        return diagnostic!;
    }

    private static void NotReported(string verb, string route, string parameters) {
        Assert.DoesNotContain(
            Generate(verb, route, parameters).GeneratorDiagnostics,
            reported => reported.Id == DiagnosticId);
    }

    /// <summary>The trial's exact repro.</summary>
    [Fact]
    public void ATokenDifferingOnlyByCaseIsAnError() {
        Assert.Equal(
            DiagnosticSeverity.Error,
            Reported("Get", "/events/{eventid}", "string eventId").Severity);
    }

    /// <summary>
    /// The message names both identifiers and says which way to fix it. Naming only the token
    /// leaves the reader to spot a difference of one character.
    /// </summary>
    [Fact]
    public void TheMessageNamesBothIdentifiersAndTheCaseDifference() {
        var message = Reported("Get", "/events/{eventid}", "string eventId").GetMessage();

        Assert.Contains("{eventid}", message);
        Assert.Contains("eventId", message);
        Assert.Contains("only by case", message);
        Assert.Contains("EventController", message);
        Assert.Contains("Handle", message);
    }

    /// <summary>
    /// A case difference is never intentional, so the verb does not matter - a PUT with the same
    /// typo silently takes two readings of one body.
    /// </summary>
    [Fact]
    public void ACaseDifferenceIsReportedOnABodyCarryingVerbToo() {
        Assert.Equal(
            DiagnosticSeverity.Error,
            Reported("Put", "/events/{eventid}", "string eventId, EventBody body").Severity);
    }

    /// <summary>
    /// A misspelling that is not merely a case difference reaches the same place, and the message
    /// says what the verb costs it.
    /// </summary>
    [Fact]
    public void AMisspeltTokenOnABodylessVerbIsAnError() {
        var message = Reported("Get", "/events/{eventKey}", "string eventId").GetMessage();

        Assert.Contains("{eventKey}", message);
        Assert.Contains("eventId", message);
        Assert.Contains("carries none", message);
        Assert.Contains("FromQueryString", message);
    }

    [Theory]
    [InlineData("Get", "/events/{eventId}", "string eventId")]
    [InlineData("Get", "/events/{eventId}/holds/{holdId}", "string eventId, string holdId")]
    [InlineData("Get", "/events/{*path}", "string path")]
    [InlineData("Get", "/events/{eventId:int}", "int eventId")]
    [InlineData("Get", "/events", "")]
    [InlineData("Post", "/events", "EventBody body")]
    [InlineData("Post", "/events/{eventId}", "string eventId, EventBody body")]
    public void ARouteThatBindsWhatItDeclaresIsNotReported(
        string verb, string route, string parameters) {
        NotReported(verb, route, parameters);
    }

    /// <summary>
    /// A GET that reads nothing from the body has nothing to report, however its tokens are spelt -
    /// which is what a token declared in a shared base path and used by only some handlers under it
    /// looks like.
    /// </summary>
    [Fact]
    public void AnUnboundTokenWithNoBodyParameterIsNotReported() {
        NotReported("Get", "/events/{eventId}/holds/{holdId}", "string eventId");
    }

    /// <summary>
    /// A parameter the author placed by hand is where the author put it, and the token that binds
    /// nothing has nowhere else to have sent it.
    /// </summary>
    [Fact]
    public void AParameterBoundFromTheQueryStringIsNotABodyRead() {
        NotReported("Get", "/events/{eventKey}", "[FromQueryString(\"page\")] int page");
    }

    /// <summary>
    /// A token named after a C# keyword. The parameter can only be declared <c>@base</c>, and the
    /// match read <c>Identifier.Text</c>, which carries the escape - so the token bound nothing,
    /// the parameter went to the body, and a legal route failed to build.
    /// </summary>
    [Fact]
    public void ATokenNamedAfterAKeywordIsNotReported() {
        NotReported("Get", "/things/{base}", "string @base");
    }

    /// <summary>And the case rule still applies to one, so the escape did not turn the match off.</summary>
    [Fact]
    public void ATokenNamedAfterAKeywordDifferingByCaseIsStillAnError() {
        Assert.Equal(
            DiagnosticSeverity.Error,
            Reported("Get", "/things/{Base}", "string @base").Severity);
    }

    /// <summary>
    /// The handler is still emitted, for the reason an unsupported token leaves it emitted: the
    /// routing table would otherwise point at a class that was never written, burying the one
    /// diagnostic that says what is wrong.
    /// </summary>
    [Fact]
    public void TheHandlerIsStillEmitted() {
        var result = Generate("Get", "/events/{eventid}", "string eventId");

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("EventController_Handle"));
    }
}
