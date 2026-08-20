using Hardened.Requests.Abstract.Execution;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// <c>StoreServiceImpl</c> is the one handler in this SUT declared with a base class, and that is
/// deliberate - do not simplify it away. C# requires the base class to come first in the base list,
/// which is the exact shape the handler selector used to mis-read.
/// </summary>
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

    /// <summary>
    /// The registration itself, rather than the route that depends on it.
    /// </summary>
    /// <remarks>
    /// The selector read <c>BaseList.Types.FirstOrDefault()</c> and registered whatever it found, so
    /// this handler registered <c>StoreServiceBase</c> and <c>IStoreService</c> resolved to nothing.
    /// The route above would have caught it, but only by failing at request time with an error about
    /// a missing service - which is a long way from the cause. This says the cause.
    /// </remarks>
    [HardenedTest]
    public void TheHandlerIsRegisteredAgainstItsServiceInterfaceNotItsBaseClass(ITestWebApp testWebApp) {
        var service = testWebApp.RootServiceProvider.GetService<IStoreService>();

        Assert.NotNull(service);
        Assert.IsType<StoreServiceImpl>(service);
    }
}
