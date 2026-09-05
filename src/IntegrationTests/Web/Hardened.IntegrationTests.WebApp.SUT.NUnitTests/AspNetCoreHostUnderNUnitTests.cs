using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Web.AspNetCore.Runtime;
using NSubstitute;

namespace Hardened.IntegrationTests.WebApp.SUT.NUnitTests;

/// <summary>
/// The ASP.NET Core host under NUnit, whose runner builds the container through the attribute
/// the same way xUnit's does.
/// </summary>
[AspNetCoreRuntime]
public class AspNetCoreHostUnderNUnitTests {

    [HardenedTest]
    public async Task ARequestAnswersThroughTheAspNetPipeline(ITestWebApp app) {
        var response = await app.Get("/verbs/item/42");

        response.Assert.Ok();
        Assert.That(response.Deserialize<string>(), Is.EqualTo("got:42"));
        Assert.That(response.Headers.ContainsKey("Date"), Is.True, "a header only a server writes");
    }

    [HardenedTest]
    public async Task AMockBehindARouteIsTheOneTheHandlerSees(ITestWebApp app, [Mock] IMathService<int> math) {
        math.Add(Arg.Any<int[]>()).Returns(100);

        var response = await app.Post(new MathAddModel { Values = [1, 2, 3] }, "/int/add");

        response.Assert.Ok();
        Assert.That(response.Deserialize<int>(), Is.EqualTo(100));
    }

    [HardenedTest]
    public async Task AnUnmatchedPathIsAspNetsOwn404(ITestWebApp app) {
        var response = await app.Get("/no/such/route");

        response.Assert.NotFound();
        Assert.That(await response.ReadTextAsync(), Is.Empty);
    }
}
