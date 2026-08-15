namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A constrained route token, end to end.
/// </summary>
/// <remarks>
/// The point of the constraint is the status code. <c>/binding/path-typed/abc</c> reaches the
/// handler and answers 400 - the route matched and the binder failed - which reads as "you
/// addressed a real endpoint incorrectly". <c>/binding/path-constrained/abc</c> is a 404, which is
/// the truthful answer: there is no resource at that URL. It also rejects the value before any
/// filter or binder runs.
/// </remarks>
public class RouteConstraintTests {

    [HardenedTest]
    public async Task AConstrainedTokenMatchesAValueThatPasses(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path-constrained/21");

        response.Assert.Ok();
        Assert.Equal(42, response.Deserialize<int>());
    }

    [HardenedTest]
    public async Task AConstrainedTokenIs404ForAValueThatFails(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path-constrained/abc");

        response.Assert.NotFound();
    }

    /// <summary>
    /// The unconstrained shape beside it still answers 400, which is the distinction the
    /// documented rule turns on: use the constraint when a wrong value means "no such URL", leave
    /// it off when the value is input being validated.
    /// </summary>
    [HardenedTest]
    public async Task TheUnconstrainedShapeStillAnswers400(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path-typed/abc");

        response.Assert.BadRequest();
    }

    [HardenedTest]
    public async Task ADeclaredConstraintMatchesAValueThatPasses(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path-code/ABC");

        response.Assert.Ok();
        Assert.Equal("ABC", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task ADeclaredConstraintIs404ForAValueThatFails(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/path-code/abc");

        response.Assert.NotFound();
    }

    /// <summary>
    /// A catch-all token binds too, by the name the token declares.
    /// </summary>
    /// <remarks>
    /// It did not. Whether a parameter came from the path was decided by looking for the literal
    /// text <c>{name}</c> in the template, which finds neither <c>{name:int}</c> nor
    /// <c>{*name}</c> - so both bound from the request body and answered 500 on a GET with no
    /// body, from a route that matched perfectly. Found by adding the first constrained route to
    /// this fixture.
    /// </remarks>
    [HardenedTest]
    public async Task ACatchAllTokenBindsFromThePath(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/binding/files/a/b/c.txt");

        response.Assert.Ok();
        Assert.Equal("a/b/c.txt", response.Deserialize<string>());
    }
}
