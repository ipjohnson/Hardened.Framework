using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Web.Runtime.Responses;
using NSubstitute;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Static handlers answered by the application the tests actually build.
/// </summary>
/// <remarks>
/// The generator suite proves the emitted file compiles. This proves the container serves it: a
/// static handler's declaring type is registered by nobody, so the filter at HandlerCreation has to
/// stand down rather than refuse the request.
/// </remarks>
public class StaticHandlerTests
{
    [HardenedTest]
    public async Task AStaticHandlerAnswers(ITestWebApp app)
    {
        var response = await app.Get("/static/echo/hello");

        response.Assert.Ok();

        Assert.Equal("hello", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task AStaticHandlerResolvesItsServiceParameters(ITestWebApp app)
    {
        var model = new MathAddModel
        {
            Values = new List<int> { 1, 2, 3 },
        };

        var response = await app.Post(model, "/static/add");

        response.Assert.Ok();

        Assert.Equal(6, response.Deserialize<int>());
    }

    /// <summary>
    /// And resolves them per request, out of the same scope an instance handler's constructor
    /// would have been built from.
    /// </summary>
    [HardenedTest]
    public async Task AStaticHandlersServiceParameterIsTheRegisteredOne(
        ITestWebApp app,
        [Mock] IMathService<int> mockService
    )
    {
        mockService.Add(Arg.Any<int[]>()).Returns(100);

        var model = new MathAddModel
        {
            Values = new List<int> { 1, 2, 3 },
        };

        var response = await app.Post(model, "/static/add");

        response.Assert.Ok();

        Assert.Equal(100, response.Deserialize<int>());
    }

    [HardenedTest]
    public async Task AnAsyncStaticHandlerAnswers(ITestWebApp app)
    {
        var response = await app.Get("/static/async/waited");

        response.Assert.Ok();

        Assert.Equal("waited", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task AControllerHoldingBothKindsServesBoth(ITestWebApp app)
    {
        var stat = await app.Get("/mixed/static/value");

        stat.Assert.Ok();

        Assert.Equal("value", stat.Deserialize<string>());

        var instance = await app.Post(
            new MathAddModel
            {
                Values = new List<int> { 4, 5 },
            },
            "/mixed/instance"
        );

        instance.Assert.Ok();

        Assert.Equal(9, instance.Deserialize<int>());
    }
}
