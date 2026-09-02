using Hardened.Requests.Runtime.Validation;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Validation on a hand-written controller, end to end.
/// </summary>
/// <remarks>
/// <para>
/// The claim under test is that declaring constraints is the whole of the work. Nothing in
/// <c>RegistrationController</c> mentions validation - no <c>[Validate&lt;T&gt;]</c>, no filter, no
/// registration - and the constraints live on the model it binds. Until the web generator emitted a
/// validator for the handler's <c>Parameters</c> class and attached a filter to run it, every
/// request below came back 200: the model's validator was generated and registered, and no code
/// path ever called it.
/// </para>
/// <para>
/// It is an integration test rather than a generator test because a green build proves none of it.
/// The filter has to be in the chain, the parameters object has to be the type the filter is typed
/// on, and <see cref="ValidationException"/> has to reach the converter that turns it into a 400 -
/// three things that fail silently and are invisible to a test that reads generated source.
/// </para>
/// </remarks>
public class RegistrationValidationTests {

    [HardenedTest]
    public async Task MissingRequiredField_Returns400(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { Name = "", Age = 30 }, "/registration");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.NotNull(error);
        Assert.Equal("ValidationError", error.Type);
        Assert.Contains(error.Errors, e => e.Field == "model.name");
    }

    /// <summary>
    /// A DataAnnotations constraint and a ValidationModules one on the same model, failing in the
    /// same request. Neither the response nor the field path says which vocabulary declared it.
    /// </summary>
    [HardenedTest]
    public async Task BothConstraintVocabulariesReportTheSameWay(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { Name = "ab", Age = 7 }, "/registration");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error!.Errors, e => e.Field == "model.name" && e.Code == "string_length");
        Assert.Contains(error.Errors, e => e.Field == "model.age" && e.Code == "range");
    }

    /// <summary>
    /// Nesting, which is what the parameters validator does rather than checks itself: it descends
    /// into the body and calls the validator emitted for that model, which descends again.
    /// </summary>
    [HardenedTest]
    public async Task NestedModelFailuresCarryTheirPath(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new { Name = "Valid", Age = 30, Address = new { City = "", Country = "USA" } },
            "/registration");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error!.Errors, e => e.Field.EndsWith("city"));
        Assert.Contains(error.Errors, e => e.Field.EndsWith("country") && e.Code == "string_length");
    }

    /// <summary>
    /// The body is pathed under the parameter it arrived as, so a body field and a route token that
    /// share a name stay distinguishable - the same reason the spec path reports <c>body.name</c>.
    /// </summary>
    [HardenedTest]
    public async Task BodyErrorsArePathedUnderTheParameterName(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { Name = "", Age = 30 }, "/registration/for/acme");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error!.Errors, e => e.Field == "model.name");
    }

    /// <summary>
    /// The other half of the contract. A filter that rejected everything would pass every test
    /// above.
    /// </summary>
    [HardenedTest]
    public async Task ValidRequestStillSucceeds(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new { Name = "Whiskers", Age = 30, Address = new { City = "Boston", Country = "US" } },
            "/registration");

        response.Assert.Ok();
    }

    /// <summary>
    /// Absent optional structure is not a failure: <c>Address</c> is unconstrained on its own, and
    /// the constraints inside it apply to an address that was sent.
    /// </summary>
    [HardenedTest]
    public async Task OmittingAnOptionalNestedModelIsFine(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { Name = "Whiskers", Age = 30 }, "/registration");

        response.Assert.Ok();
    }

    /// <summary>
    /// A handler whose types constrain nothing gets no filter, and has to keep working. Attachment
    /// is per-handler rather than blanket - a filter on every handler would put validation's cost
    /// on requests with nothing to validate.
    /// </summary>
    [HardenedTest]
    public async Task AnUnconstrainedHandlerIsUnaffected(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { Values = new[] { 1, 2, 3 } }, "/registration/anonymous");

        response.Assert.Ok();
    }

    #region an operation that declares its validation status

    /// <summary>
    /// H-08. A spec-first operation declares 422 and the runtime honours it; code-first had no way
    /// to say the same thing, and the trial's 422 requirement took a marker attribute plus a
    /// wrapping <c>IExceptionToModelConverter</c> to satisfy - while the served document still
    /// carried an unreachable 400 beside the hand-declared 422.
    /// </summary>
    /// <remarks>
    /// Derived from <c>[Throws&lt;RequestValidationError&gt;(422)]</c> rather than declared twice.
    /// The handler has already put that status in the document; reading the same declaration for
    /// the runtime is what closes the gap without a second source of truth on a verb attribute.
    /// </remarks>
    [HardenedTest]
    public async Task AConstraintFailureAnswersTheDeclaredStatus(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new { Name = "", Age = 30 }, "/registration/declared-422");

        Assert.Equal(422, response.StatusCode);
        Assert.Contains(
            response.Deserialize<RequestValidationError>().Errors, e => e.Field == "model.name");
    }

    /// <summary>A handler validating by hand reaches the same status.</summary>
    [HardenedTest]
    public async Task AThrownValidationExceptionAnswersTheDeclaredStatus(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new { Name = "Ada", Age = 30 }, "/registration/declared-422/by-hand");

        Assert.Equal(422, response.StatusCode);
    }

    /// <summary>
    /// And so does a body the deserializer refuses, which is the half that split the status in two
    /// on every spec-first operation declaring 422 until the converter looked it up.
    /// </summary>
    [HardenedTest]
    public async Task ABodyTheDeserializerRefusesAnswersTheDeclaredStatus(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new { Name = "Ada", Age = "not a number" }, "/registration/declared-422");

        Assert.Equal(422, response.StatusCode);
        Assert.Equal(
            "ValidationError", response.Deserialize<RequestValidationError>().Type);
    }

    /// <summary>
    /// The control. An operation that declares nothing still answers the stock 400, so the
    /// derivation reaches the operation that asked for it and no other.
    /// </summary>
    [HardenedTest]
    public async Task AnOperationDeclaringNothingStillAnswers400(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { Name = "", Age = 30 }, "/registration");

        response.Assert.BadRequest();
    }

    #endregion
}
