namespace Hardened1.Tests;

/// <summary>
/// ITestWebApp sends a request through the real pipeline - routing, filters, binding, the
/// handler and serialisation - with no socket, port or running host.
/// </summary>
public class GreetingControllerTests {

    /// <summary>
    /// The response shape as a client sees it, declared here rather than reused from the
    /// application - so this test reads the same whether the model was hand-written or generated
    /// from a contract, and asserts on the wire rather than on an internal type.
    /// </summary>
    private record GreetingResponse(string Message);

    [HardenedTest]
    public async Task GreetsByName(ITestWebApp app) {
        var response = await app.Get("/greeting/world");

        response.Assert.Ok();

        Assert.Equal("Hello, world!", response.Deserialize<GreetingResponse>().Message);
    }

    /// <summary>A path the application does not declare is a 404, not a 200 with nothing in it.</summary>
    [HardenedTest]
    public async Task AnUndeclaredPathIsNotFound(ITestWebApp app) {
        (await app.Get("/greeting")).Assert.NotFound();
    }

#if (specFirst)
    /// <summary>
    /// The constraints in the contract are enforced before the handler runs.
    /// </summary>
    /// <remarks>
    /// Nothing in this project validates anything. maxLength on the path parameter became a
    /// filter in front of the generated handler, so a value too long never reaches the code.
    /// </remarks>
    [HardenedTest]
    public async Task AValueTheContractDisallowsIsRejected(ITestWebApp app) {
        var response = await app.Get("/greeting/" + new string('x', 100));

        Assert.Equal(400, response.StatusCode);
    }
#endif
}
