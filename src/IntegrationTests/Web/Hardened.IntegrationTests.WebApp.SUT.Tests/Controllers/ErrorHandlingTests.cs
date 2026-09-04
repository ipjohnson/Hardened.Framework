using Hardened.Requests.Abstract.Errors;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Exception-to-status mapping driven through the real pipeline: routing, handler
/// invocation, the exception filter, the converter and response serialization together.
/// Unit tests cover the converter in isolation; these confirm the wiring around it.
/// </summary>
public class ErrorHandlingTests {

    [HardenedTest]
    public async Task BadRequestExceptionBecomes400(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/bad-request");

        response.Assert.BadRequest();
    }

    /// <summary>
    /// A consumer-defined exception is a client error because of what it derives from, not
    /// what it is called.
    /// </summary>
    [HardenedTest]
    public async Task ExceptionDerivedFromBadRequestBecomes400(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/derived-client-error");

        response.Assert.BadRequest();
    }

    [HardenedTest]
    public async Task FormatExceptionBecomes400(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/format");

        response.Assert.BadRequest();
    }

    /// <summary>
    /// A 415 with the codings the server accepts, as RFC 9110 specifies. This was a 400 while the
    /// JSON deserializers did the decoding; the request decompression filter changed both.
    /// </summary>
    [HardenedTest]
    public async Task UnsupportedContentEncodingBecomes415NamingWhatIsAccepted(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/bad-encoding");

        Assert.Equal(415, response.StatusCode);
        Assert.Equal("gzip, br", response.Headers["Accept-Encoding"].ToString());
    }

    [HardenedTest]
    public async Task UnrecognisedExceptionBecomes500(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/server");

        Assert.Equal(500, response.StatusCode);
    }

    /// <summary>
    /// The regression this guards: classification used to be a substring match on the type
    /// name, so BadgeNotFoundException was served as a 400 purely because its name contains
    /// "Bad". It derives from Exception, so it must be a 500.
    /// </summary>
    [HardenedTest]
    public async Task ExceptionMerelyNamedLikeAClientErrorBecomes500(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/badge-missing");

        Assert.Equal(500, response.StatusCode);
    }

    /// <summary>
    /// The response body carries a serialized ErrorModel, not just a status code.
    /// </summary>
    [HardenedTest]
    public async Task ErrorResponseCarriesASerialisedModel(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/server");

        var error = response.Deserialize<ErrorModel>();

        Assert.Equal("ServerError", error.Type);
        Assert.NotEqual("", error.Message);
    }

    /// <summary>
    /// And it carries nothing of the exception that caused it.
    /// </summary>
    /// <remarks>
    /// The handler throws <c>InvalidOperationException("the widget was not ready")</c>. Neither the
    /// type nor the message reaches the caller, because neither was written for one. The exception
    /// is still logged in full through <c>IRequestLogger.RequestFailed</c>.
    /// </remarks>
    [HardenedTest]
    public async Task AServerErrorRevealsNothingAboutTheException(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/server");

        var error = response.Deserialize<ErrorModel>();

        Assert.DoesNotContain("the widget was not ready", error.Message);
        Assert.DoesNotContain(nameof(InvalidOperationException), error.Type);
    }

    /// <summary>
    /// A body this service cannot read is a 400 with a field-level list, like every other bad value
    /// in the same body - not a 500.
    /// </summary>
    /// <remarks>
    /// Driven as raw text rather than an object, because the point is a payload the deserializer
    /// refuses. This answered 500 and echoed the parser's message before.
    /// </remarks>
    [HardenedTest]
    public async Task AnUnreadableRequestBodyBecomes400(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "{\"values\": }", "/binding/body/totals",
            request => request.Headers["Content-Type"] = "application/json");

        response.Assert.BadRequest();
    }

    /// <summary>
    /// And it names the failure as a field under the handler's own parameter identifier, the way
    /// a failed constraint in the same body does. The prefix was a hardcoded "body", so the
    /// deserializer and the validators disagreed about the same member's path on any handler
    /// that named its parameter something else - this one calls it <c>model</c>.
    /// </summary>
    [HardenedTest]
    public async Task AnUnreadableRequestBodyCarriesAFieldLevelError(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "{\"values\": [\"not a number\"]}", "/binding/body/totals",
            request => request.Headers["Content-Type"] = "application/json");

        response.Assert.BadRequest();

        var error = response.Deserialize<ValidationShape>();

        Assert.Equal("ValidationError", error!.Type);
        Assert.NotEmpty(error.Errors);
        Assert.StartsWith("model", error.Errors[0].Field);
    }

    private record ValidationShape(string Type, string Message, List<FieldShape> Errors);

    private record FieldShape(string Field, string Code, string Message);

    /// <summary>
    /// A status the pipeline had no way to produce. Every thrown exception was classified as 400 or
    /// 500, so a specification declaring a 404 with its own payload could not be honoured whatever
    /// the handler did.
    /// </summary>
    [HardenedTest]
    public async Task AStatusCodeExceptionCarriesItsOwnStatus(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/declared-status");

        Assert.Equal(404, response.StatusCode);
    }

    /// <summary>And the body the specification declared for it, rather than the generic model.</summary>
    [HardenedTest]
    public async Task AStatusCodeExceptionCarriesItsDeclaredBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/declared-body");

        Assert.Equal(409, response.StatusCode);

        var body = response.Deserialize<ConflictShape>();

        Assert.Equal("locked", body!.Code);
        Assert.Equal("held by another writer", body.Message);
    }

    private record ConflictShape(string Code, string Message);

    // ------------------------------------------------------------- a committed content type

    /// <summary>
    /// [RawResponse] commits the content type before the handler runs, so it is still on the
    /// response when the handler throws - and the raw writer cannot carry an error model. The
    /// error is recommitted to JSON instead of the locator's refusal escaping as an empty 500.
    /// </summary>
    [HardenedTest]
    public async Task ARawResponseHandlerThatThrowsStillAnswersItsDeclaredStatus(
        ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/raw-declared-status");

        Assert.Equal(409, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);

        var body = response.Deserialize<ConflictShape>();

        Assert.Equal("locked", body!.Code);
    }

    /// <summary>The same rescue for an unclassified fault: a 500 with a body, in JSON.</summary>
    [HardenedTest]
    public async Task ARawResponseHandlerThatFaultsAnswersAJsonServerError(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/raw-server-error");

        Assert.Equal(500, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);

        var error = response.Deserialize<ErrorModel>();

        Assert.Equal("ServerError", error.Type);
    }
}
