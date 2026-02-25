namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

public class StoreControllerTests {
    [HardenedTest]
    public async Task ListStores_ReturnsListOfStores(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/stores");

        response.Assert.Ok();

        var stores = response.Deserialize<List<Store>>();
        Assert.NotNull(stores);
        Assert.Equal(2, stores.Count);
        Assert.Contains(stores, s => s.Name == "Downtown");
        Assert.Contains(stores, s => s.Name == "Mall");
    }
}
