using Hardened.Requests.Runtime.Validation;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// Validation, end to end: a request that violates the specification's constraints reaches a 400
/// carrying the fields that failed.
/// </summary>
/// <remarks>
/// <para>
/// Everything else about this arrangement is checked at compile time - that the task emits the
/// attributes, that the generator emits validators and registers them, that the handler is attached
/// to a filter. None of that proves a request is ever validated. Four things have to line up at run
/// time and each fails silently: the validators have to be registered into this application's entry
/// point, <c>Parameters</c> has to implement the interface the filter is typed on, the filter has to
/// be in the chain, and <see cref="ValidationException"/> has to reach the converter that writes the
/// response. A green build says nothing about any of them.
/// </para>
/// </remarks>
public class ValidationTests {

    /// <summary>
    /// A body property the spec marks required. The path is <c>body.name</c> rather than
    /// <c>name</c>: the payload is reached by descending into the parameters' <c>body</c> member, so
    /// a caller can tell a body failure from a path parameter of the same name.
    /// </summary>
    [HardenedTest]
    public async Task CreatePet_MissingRequiredName_Returns400(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new CreatePetRequest("", "cat"), "/pets");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.NotNull(error);
        Assert.Equal("ValidationError", error.Type);
        Assert.Contains(error.Errors, e => e.Field == "body.name");
    }

    /// <summary>
    /// maxLength on a body property. The name is present, so this is the constraint after
    /// <c>[Required]</c> rather than instead of it.
    /// </summary>
    [HardenedTest]
    public async Task CreatePet_NameTooLong_Returns400(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new CreatePetRequest(new string('a', 101), "cat"), "/pets");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error!.Errors, e => e.Field == "body.name" && e.Code == "string_length");
    }

    /// <summary>
    /// The pattern, which is the reason the spec read moved to a build task at all: this goes
    /// through a <c>[GeneratedRegex]</c> member rather than a Regex built at run time.
    /// </summary>
    [HardenedTest]
    public async Task CreatePet_TagViolatesPattern_Returns400(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new CreatePetRequest("Whiskers", "not a tag!"), "/pets");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error!.Errors, e => e.Field == "body.tag" && e.Code == "pattern");
    }

    /// <summary>
    /// A query parameter, which stays bare. Path and query are both the URL as far as a caller is
    /// concerned, so making them say which they came from would be a distinction to decode without
    /// wanting it.
    /// </summary>
    [HardenedTest]
    public async Task ListPets_LimitAboveMaximum_Returns400(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?limit=500");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error!.Errors, e => e.Field == "limit" && e.Code == "range");
    }

    /// <summary>
    /// A value that will not parse is a failure, not an absent value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to return 200. <c>ParseOptional</c> caught the parse failure and returned null, so
    /// <c>?limit=abc</c> and no <c>limit</c> at all were the same request - the handler ran with the
    /// parameter unset, and the <c>maximum</c> the spec puts on it was never evaluated because there
    /// was no longer a number to evaluate it against.
    /// </para>
    /// <para>
    /// It reports through the same field-level shape a constraint failure does. A caller who sent
    /// <c>limit=abc</c> and one who sent <c>limit=500</c> made the same kind of mistake, and only the
    /// code distinguishes them.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task ListPets_LimitNotANumber_Returns400(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?limit=abc");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Equal("ValidationError", error!.Type);
        Assert.Contains(error.Errors, e => e.Field == "limit" && e.Code == "invalid");
    }

    /// <summary>
    /// Omitting an optional parameter is still fine - the point above is about malformed values, not
    /// about making everything mandatory.
    /// </summary>
    [HardenedTest]
    public async Task ListPets_WithoutLimit_StillSucceeds(ITestWebApp testWebApp) {
        (await testWebApp.Get("/pets")).Assert.Ok();
    }

    [HardenedTest]
    public async Task SearchPets_QueryTooShort_Returns400(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/search?q=a");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error!.Errors, e => e.Field == "q");
    }

    /// <summary>
    /// Every failing constraint is reported, not just the first. A caller fixing one field at a time
    /// because the server only ever names one is the thing this avoids.
    /// </summary>
    [HardenedTest]
    public async Task CreatePet_SeveralViolations_ReportsAllOfThem(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new CreatePetRequest(new string('a', 101), "not a tag!"), "/pets");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error!.Errors, e => e.Field == "body.name");
        Assert.Contains(error.Errors, e => e.Field == "body.tag");
    }

    /// <summary>
    /// The other half of the contract: a request that satisfies the constraints is not touched. A
    /// filter that rejected everything would pass every test above.
    /// </summary>
    [HardenedTest]
    public async Task CreatePet_Valid_StillSucceeds(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new CreatePetRequest("Whiskers", "cat"), "/pets");

        response.Assert.Ok();
    }

    [HardenedTest]
    public async Task ListPets_LimitWithinRange_StillSucceeds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?limit=50");

        response.Assert.Ok();
    }

    /// <summary>
    /// An operation the spec constrains nothing about gets no filter at all, and has to keep
    /// working - the attachment is per-operation rather than blanket.
    /// </summary>
    [HardenedTest]
    public async Task AnUnconstrainedOperationIsUnaffected(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/stores");

        response.Assert.Ok();
    }
}
