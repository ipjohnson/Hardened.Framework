using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Web.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.DependencyInjection;

/// <summary>
/// The seam an application uses to add a filter to every handler.
///
/// <para>
/// The action is captured at registration and replayed against the registry once the root
/// provider exists, because a global filter usually needs services that are not resolvable while
/// the collection is still being built. Running it too early, or never, means an authentication
/// or logging filter an application believes it installed is simply absent.
/// </para>
/// </summary>
public class FilterRegistryStartupServiceTests {

    [Fact]
    public async Task TheRegistrationActionRunsAgainstTheResolvedRegistry() {
        var registry = Substitute.For<IGlobalFilterRegistry>();
        var filter = Substitute.For<IExecutionFilter>();

        var provider = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        await new FilterRegistryStartupService(r => r.RegisterFilter(filter, 100)).Startup(provider);

        registry.Received(1).RegisterFilter(filter, 100);
    }

    /// <summary>
    /// The action is not run when the service is constructed — only when startup reaches it. An
    /// action that ran at construction would see a registry that does not exist yet.
    /// </summary>
    [Fact]
    public void TheRegistrationActionDoesNotRunAtConstruction() {
        var calls = 0;

        _ = new FilterRegistryStartupService(_ => calls++);

        Assert.Equal(0, calls);
    }

    /// <summary>Startup reports success, which is what lets the rest of the sequence continue.</summary>
    [Fact]
    public async Task StartupReportsSuccess() {
        var provider = new ServiceCollection()
            .AddSingleton(Substitute.For<IGlobalFilterRegistry>())
            .BuildServiceProvider();

        Assert.True(await new FilterRegistryStartupService(_ => { }).Startup(provider));
    }

    /// <summary>
    /// The registry is resolved as required rather than optionally. An application whose
    /// composition dropped the registry finds out at startup rather than on the first request that
    /// silently ran without its global filters.
    /// </summary>
    [Fact]
    public async Task AMissingRegistryFailsStartupRatherThanSkippingTheFilters() {
        var provider = new ServiceCollection().BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new FilterRegistryStartupService(_ => { }).Startup(provider));
    }

    /// <summary>
    /// The action is free to register more than one filter, and each reaches the registry with its
    /// own order.
    /// </summary>
    [Fact]
    public async Task EveryFilterTheActionRegistersReachesTheRegistry() {
        var registry = Substitute.For<IGlobalFilterRegistry>();
        var first = Substitute.For<IExecutionFilter>();
        var second = Substitute.For<IExecutionFilter>();

        var provider = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        await new FilterRegistryStartupService(r => {
            r.RegisterFilter(first, 10);
            r.RegisterFilter(second, 20);
        }).Startup(provider);

        registry.Received(1).RegisterFilter(first, 10);
        registry.Received(1).RegisterFilter(second, 20);
    }
}
