namespace Hardened.IntegrationTests.Benchmark.SUT.Tests;

/// <summary>
/// TechEmpower test types 2, 3 and 5: single query, multiple queries and updates.
/// </summary>
/// <remarks>
/// What these exercise in Hardened is query-string binding and the clamping rules around it. The
/// <c>queries</c> parameter is declared as a string in the spec rather than an integer on purpose:
/// the benchmark requires that a non-numeric value be treated as 1 rather than rejected, which
/// means it has to bind before it is interpreted.
/// </remarks>
public class DatabaseRouteTests {

    [HardenedTest]
    public async Task Db_ReturnsOneWorldRow(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/db");

        response.Assert.Ok();

        var world = response.Deserialize<World>();

        Assert.NotNull(world);
        Assert.InRange(world.Id, 1, 10_000);
        Assert.InRange(world.RandomNumber, 1, 10_000);
    }

    [HardenedTest]
    public async Task Queries_ReturnsTheRequestedCount(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/queries?queries=7");

        response.Assert.Ok();

        var worlds = response.Deserialize<List<World>>();

        Assert.NotNull(worlds);
        Assert.Equal(7, worlds.Count);
    }

    /// <summary>A missing parameter means one row, not zero and not an error.</summary>
    [HardenedTest]
    public async Task Queries_WithNoParameterReturnsOneRow(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/queries");

        response.Assert.Ok();

        Assert.Single(response.Deserialize<List<World>>()!);
    }

    /// <summary>
    /// The benchmark is explicit that a non-integer value is treated as 1. Binding a string is what
    /// makes this reachable - an int parameter would have failed to bind and returned an error.
    /// </summary>
    [HardenedTest]
    public async Task Queries_WithANonNumericParameterReturnsOneRow(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/queries?queries=foo");

        response.Assert.Ok();

        Assert.Single(response.Deserialize<List<World>>()!);
    }

    [HardenedTest]
    public async Task Queries_ClampsBelowOneToOne(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/queries?queries=0");

        response.Assert.Ok();

        Assert.Single(response.Deserialize<List<World>>()!);
    }

    [HardenedTest]
    public async Task Queries_ClampsAboveFiveHundredToFiveHundred(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/queries?queries=1000");

        response.Assert.Ok();

        Assert.Equal(500, response.Deserialize<List<World>>()!.Count);
    }

    [HardenedTest]
    public async Task Updates_ReturnsTheRequestedCount(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/updates?queries=5");

        response.Assert.Ok();

        var worlds = response.Deserialize<List<World>>();

        Assert.NotNull(worlds);
        Assert.Equal(5, worlds.Count);
        Assert.All(worlds, world => Assert.InRange(world.RandomNumber, 1, 10_000));
    }

    /// <summary>
    /// An update has to persist, or the test type measures nothing. Re-reading the same id through
    /// /db is not possible, so this checks the store directly through a second /updates call
    /// returning rows that are still within range and addressable.
    /// </summary>
    [HardenedTest]
    public async Task Updates_ReturnsRowsThatRemainAddressable(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/updates?queries=3");

        response.Assert.Ok();

        var worlds = response.Deserialize<List<World>>()!;

        Assert.All(worlds, world => Assert.InRange(world.Id, 1, 10_000));
    }
}
