using Hardened1;

namespace Hardened1.Tests;

/// <summary>
/// ITestWebApp sends a request through the real pipeline - routing, filters, binding, the
/// handler and serialisation - with no socket, port or running host.
/// </summary>
public class GreetingControllerTests {

    [HardenedTest]
    public async Task GreetsByName(ITestWebApp app) {
        var response = await app.Get("/greeting/world");

        response.Assert.Ok();

        Assert.Equal("Hello, world!", response.Deserialize<Greeting>().Message);
    }

    /// <summary>A path the application does not declare is a 404, not a 200 with nothing in it.</summary>
    [HardenedTest]
    public async Task AnUndeclaredPathIsNotFound(ITestWebApp app) {
        (await app.Get("/greeting")).Assert.NotFound();
    }
}
