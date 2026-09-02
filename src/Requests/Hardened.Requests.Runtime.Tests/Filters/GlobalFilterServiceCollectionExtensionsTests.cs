using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// Registering a filter provider as a service, and the predicate that decides which handlers it
/// covers.
/// </summary>
/// <remarks>
/// <see cref="GlobalFilterRegistry"/> has taken <c>IEnumerable&lt;IRequestFilterProvider&gt;</c> in
/// its constructor since it was written, and nothing in the shipping framework ever registered one
/// - every global filter arrived through <c>RegisterFilter</c> instead, so the injected list was
/// empty in every application. The path worked by construction and had no test behind it. This is
/// that test.
/// </remarks>
public class GlobalFilterServiceCollectionExtensionsTests {

    private static IExecutionRequestHandlerInfo Handler(
        string path = "/orders", string method = "GET") =>
        new ExecutionRequestHandlerInfo(
            path, method, typeof(GlobalFilterServiceCollectionExtensionsTests), "Invoke");

    private static GlobalFilterRegistry RegistryFrom(IServiceCollection services) =>
        new(services.BuildServiceProvider().GetServices<IRequestFilterProvider>());

    [Fact]
    public void AProviderRegisteredAsAServiceReachesTheRegistry() {
        var services = new ServiceCollection();

        services.AddGlobalFilter(new StubProvider());

        Assert.Single(RegistryFrom(services).GetFilters(Handler()));
    }

    [Fact]
    public void APredicateThatDeclinesInstallsNothing() {
        var services = new ServiceCollection();

        services.AddGlobalFilter(new StubProvider(), when: info => info.Method == "GET");

        Assert.Empty(RegistryFrom(services).GetFilters(Handler(method: "POST")));
    }

    [Fact]
    public void APredicateThatAdmitsInstallsTheProvidersFilters() {
        var services = new ServiceCollection();

        services.AddGlobalFilter(new StubProvider(), when: info => info.Method == "GET");

        Assert.Single(RegistryFrom(services).GetFilters(Handler(method: "GET")));
    }

    /// <summary>
    /// A provider yielding two filters keeps both. This is why the extension wraps a provider rather
    /// than going through <c>RegisterFilter</c>, which takes a function returning one nullable
    /// filter and would drop everything past the first.
    /// </summary>
    [Fact]
    public void AProviderYieldingTwoFiltersKeepsBoth() {
        var services = new ServiceCollection();

        services.AddGlobalFilter(new StubProvider(count: 2), when: _ => true);

        Assert.Equal(2, RegistryFrom(services).GetFilters(Handler()).Count);
    }

    /// <summary>
    /// Two registrations are two providers, so a second does not replace the first.
    /// </summary>
    [Fact]
    public void TwoRegistrationsBothApply() {
        var services = new ServiceCollection();

        services.AddGlobalFilter(new StubProvider());
        services.AddGlobalFilter(new StubProvider());

        Assert.Equal(2, RegistryFrom(services).GetFilters(Handler()).Count);
    }

    private sealed class StubProvider : IRequestFilterProvider {
        private readonly int _count;

        public StubProvider(int count = 1) {
            _count = count;
        }

        public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
            for (var i = 0; i < _count; i++) {
                yield return new RequestFilterInfo(_ => Substitute.For<IExecutionFilter>());
            }
        }
    }
}
