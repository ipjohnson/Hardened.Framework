using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.SUT;

[Handler]
public class StoreServiceImpl : IStoreService {
    public Task<List<Store>> ListStores() {
        return Task.FromResult(new List<Store> {
            new Store("1", "Downtown", "123 Main St"),
            new Store("2", "Mall", "456 Oak Ave")
        });
    }
}
