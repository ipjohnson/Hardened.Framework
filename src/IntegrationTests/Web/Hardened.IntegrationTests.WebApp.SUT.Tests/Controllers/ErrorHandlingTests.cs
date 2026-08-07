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

    [HardenedTest]
    public async Task UnsupportedContentEncodingBecomes400(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/errors/bad-encoding");

        response.Assert.BadRequest();
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

        Assert.Equal(nameof(InvalidOperationException), error.Type);
        Assert.Equal("the widget was not ready", error.Message);
    }
}
