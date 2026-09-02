using System.Text.Json;
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

    #region the name a caller can act on

    private const string ValidOrder =
        """{"species":"cat","weightGrams":3000,"lines":[{"sku":"TLS-0001","quantity":2}]}""";

    /// <summary>
    /// OR-04. <c>Idempotency-Key</c> failing its pattern reported <c>"field": "idempotencyKey"</c>
    /// - a name that appears nowhere in the request or the contract. Body failures already used
    /// wire names with indexed paths; header and query failures leaked the C# identifier the
    /// generator allocated.
    /// </summary>
    [HardenedTest]
    public async Task AHeaderFailureNamesTheHeader(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            ValidOrder, "/orders", request => request.Headers["Idempotency-Key"] = "not-hex");

        Assert.Equal(400, response.StatusCode);

        var error = response.Deserialize<RequestValidationError>();

        Assert.Contains(error.Errors, e => e.Field == "Idempotency-Key");
        Assert.DoesNotContain(error.Errors, e => e.Field == "idempotencyKey");
    }

    /// <summary>
    /// A valid key gets through, so the constraint is the thing being tested rather than the
    /// parameter merely being present.
    /// </summary>
    [HardenedTest]
    public async Task AValidHeaderIsAccepted(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            ValidOrder, "/orders", request => request.Headers["Idempotency-Key"] = "0f9ac3b2");

        response.Assert.Ok();
    }

    #endregion

    #region money

    /// <summary>
    /// H-10. Every <c>number</c> became a <c>double</c>, so money was a double end to end and the
    /// framework never said so.
    /// </summary>
    /// <remarks>
    /// Both spellings, because the ecosystem uses two and neither is in the OpenAPI specification:
    /// <c>number</c> + <c>decimal</c> is NSwag's, and <c>string</c> + <c>number</c> is
    /// openapi-generator's - its <c>ModelUtils.isDecimalSchema</c> tests exactly that pair.
    /// </remarks>
    [HardenedTest]
    public async Task MoneySurvivesTheRoundTripExactly(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            """{"species":"cat","weightGrams":3000,"lines":[{"sku":"TLS-0001","quantity":3,"unitPrice":19.99,"discount":0.1}]}""",
            "/orders");

        response.Assert.Ok();

        var line = response.Deserialize<OrderRequest>().Lines![0];

        // The value a double cannot hold: 19.99 * 3 is 59.97 in decimal and 59.970000000000006 in
        // binary floating point.
        Assert.Equal(19.99m, line.UnitPrice);
        Assert.Equal(59.97m, line.UnitPrice!.Value * 3);
        Assert.Equal(0.1m, line.Discount);
    }

    /// <summary>
    /// And the declared bound still runs. It is emitted as Range(Min = "0.01") - the string form
    /// ValidationModules parses against the property's own type - because rendering it through
    /// double is the one thing a decimal member exists to avoid.
    /// </summary>
    [HardenedTest]
    public async Task ABoundOnMoneyIsEnforced(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            """{"species":"cat","weightGrams":3000,"lines":[{"sku":"TLS-0001","quantity":3,"unitPrice":0.001}]}""",
            "/orders");

        Assert.Equal(400, response.StatusCode);
        Assert.Contains(
            response.Deserialize<RequestValidationError>().Errors,
            e => e.Field.Contains("unitPrice") && e.Code == "range");
    }

    /// <summary>
    /// The published document keeps the spelling the contract used, so a client generator reads
    /// back what the author wrote.
    /// </summary>
    [HardenedTest]
    public async Task TheDocumentPublishesBothSpellings(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.json");

        response.Assert.Ok();
        response.Body.Position = 0;

        // Served gzipped, because the harness asks for it by default the way a client does.
        await using var gzip = new System.IO.Compression.GZipStream(
            response.Body, System.IO.Compression.CompressionMode.Decompress);

        using var document = JsonDocument.Parse(await new StreamReader(gzip).ReadToEndAsync());

        var properties = document.RootElement
            .GetProperty("components").GetProperty("schemas")
            .GetProperty("OrderLine").GetProperty("properties");

        var unitPrice = properties.GetProperty("unitPrice");
        var discount = properties.GetProperty("discount");

        Assert.Equal("number", unitPrice.GetProperty("type").GetString());
        Assert.Equal("decimal", unitPrice.GetProperty("format").GetString());
        Assert.Equal("string", discount.GetProperty("type").GetString());
        Assert.Equal("number", discount.GetProperty("format").GetString());
    }

    #endregion
}
