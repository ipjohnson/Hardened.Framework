using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Web.Routing;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Which handlers declare a token that binds nothing while a parameter falls onto the body.
/// </summary>
/// <remarks>
/// The decision, not the reporting - the split <c>FormAndBodyConflictTests</c> uses, and for the
/// same reason. What is covered here rather than through a running generator is the described
/// front end: a spec-first parameter carries the wire name the route declares and a C# identifier
/// that may differ from it, so a rule that compared the identifier would report every described
/// path parameter whose member name was allocated.
/// </remarks>
public class RouteBindingConflictTests {

    private static ITypeDefinition Type(string name) => TypeDefinition.Get("System", name);

    private static RequestParameterInformation Parameter(
        ParameterBindType bindingType, string name, string bindingName = "") =>
        new(Type("String"), name, true, null, bindingType, bindingName, 0, null);

    private static RequestHandlerModel Handler(
        string path, string method, params RequestParameterInformation[] parameters) =>
        new(
            new RequestHandlerNameModel(path, method),
            TypeDefinition.Get("TestApp", "EventController"),
            "Handle",
            TypeDefinition.Get("TestApp.Generated", "EventController_Handle"),
            parameters,
            new ResponseInformationModel { ReturnType = Type("String") },
            []);

    /// <summary>
    /// A described parameter binds by its wire name. <c>eventId</c> in the contract becomes the
    /// member <c>EventId</c>, and the route still declares <c>{eventId}</c>.
    /// </summary>
    [Fact]
    public void ADescribedParameterBindsByItsWireName() {
        var findings = RouteBindingDiagnostics.Find(Handler(
            "/events/{eventId}", "GET",
            Parameter(ParameterBindType.Path, "EventId", "eventId")));

        Assert.Empty(findings);
    }

    /// <summary>A code-first parameter carries no wire name and binds by its identifier.</summary>
    [Fact]
    public void ACodeFirstParameterBindsByItsIdentifier() {
        var findings = RouteBindingDiagnostics.Find(Handler(
            "/events/{eventId}", "GET", Parameter(ParameterBindType.Path, "eventId")));

        Assert.Empty(findings);
    }

    [Fact]
    public void ATokenDifferingOnlyByCaseIsFound() {
        var finding = Assert.Single(RouteBindingDiagnostics.Find(Handler(
            "/events/{eventid}", "GET", Parameter(ParameterBindType.Body, "eventId"))));

        Assert.Equal("eventid", finding.Token);
        Assert.Equal("eventId", finding.BodyParameter);
        Assert.True(finding.CaseOnly);
    }

    [Fact]
    public void AMisspeltTokenOnABodylessVerbIsFound() {
        var finding = Assert.Single(RouteBindingDiagnostics.Find(Handler(
            "/events/{eventKey}", "GET", Parameter(ParameterBindType.Body, "eventId"))));

        Assert.False(finding.CaseOnly);
    }

    /// <summary>
    /// A POST reads a body because a POST carries one. Only a name that differs by case says the
    /// author meant the token.
    /// </summary>
    [Fact]
    public void AMisspeltTokenOnABodyCarryingVerbIsNotFound() {
        Assert.Empty(RouteBindingDiagnostics.Find(Handler(
            "/events/{eventKey}", "POST", Parameter(ParameterBindType.Body, "body"))));
    }

    /// <summary>
    /// DELETE is not treated as bodyless. HTTP permits a body on one and some APIs send it, so a
    /// body parameter there is a choice rather than a mistake.
    /// </summary>
    [Fact]
    public void ADeleteIsNotTreatedAsBodyless() {
        Assert.Empty(RouteBindingDiagnostics.Find(Handler(
            "/events/{eventKey}", "DELETE", Parameter(ParameterBindType.Body, "body"))));
    }

    /// <summary>
    /// A token nothing binds and nothing displaced is not a defect: a token declared in a shared
    /// base path binds nothing on the handlers under it that do not need it.
    /// </summary>
    [Fact]
    public void AnUnboundTokenWithNoBodyParameterIsNotFound() {
        Assert.Empty(RouteBindingDiagnostics.Find(Handler(
            "/tenants/{tenantId}/events/{eventId}", "GET",
            Parameter(ParameterBindType.Path, "eventId"))));
    }

    /// <summary>
    /// A dispatched handler is selected by an exact token in a header - awsJson sends every
    /// operation to <c>POST /</c> - so its path declares nothing to bind.
    /// </summary>
    [Fact]
    public void ADispatchedHandlerIsNotConsidered() {
        var model = new RequestHandlerModel(
            new RequestHandlerNameModel("/", "POST", "X-Amz-Target", "PetStore.GetPet"),
            TypeDefinition.Get("TestApp", "EventController"),
            "Handle",
            TypeDefinition.Get("TestApp.Generated", "EventController_Handle"),
            [Parameter(ParameterBindType.Body, "input")],
            new ResponseInformationModel { ReturnType = Type("String") },
            []);

        Assert.Empty(RouteBindingDiagnostics.Find(model));
    }

    /// <summary>One finding per token, so fixing the first is not how you discover the second.</summary>
    [Fact]
    public void EveryUnboundTokenIsFound() {
        var findings = RouteBindingDiagnostics.Find(Handler(
            "/events/{eventid}/holds/{holdid}", "GET",
            Parameter(ParameterBindType.Body, "eventId")));

        Assert.Equal(2, findings.Count);
    }
}
