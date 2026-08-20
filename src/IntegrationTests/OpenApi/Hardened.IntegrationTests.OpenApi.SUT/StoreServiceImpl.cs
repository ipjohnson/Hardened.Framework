using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// Something a handler might reasonably inherit - shared helpers, a logger, a base for a family of
/// services. It does nothing here except occupy the first position in the base list.
/// </summary>
public abstract class StoreServiceBase {
    protected static string Format(string name, string address) => $"{name}, {address}";
}

/// <summary>
/// A handler with a base class <em>and</em> the generated interface, which is the shape that used
/// to produce a route nobody served.
/// </summary>
/// <remarks>
/// C# requires a base class to be written first, and the generator read
/// <c>BaseList.Types.FirstOrDefault()</c> and called that "the interface the class implements". So
/// this registered <c>StoreServiceBase</c>, left <c>IStoreService</c> unimplemented by anything, and
/// built clean with no diagnostic - the route existed and failed at request time. The base list is
/// searched by name now, and <c>StoreServiceImplTests</c> drives the route to prove it.
/// </remarks>
[Handler]
public class StoreServiceImpl : StoreServiceBase, IStoreService {
    public Task<List<Store>> ListStores() {
        return Task.FromResult(new List<Store> {
            new Store("1", "Downtown", Format("Downtown", "123 Main St")),
            new Store("2", "Mall", Format("Mall", "456 Oak Ave"))
        });
    }
}
