using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Web.Runtime.Conditional;
using Hardened.Web.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Conditional;

/// <summary>
/// How the filter reaches a handler: the web module registers a provider, and the provider
/// answers for GET handlers alone.
/// </summary>
public class ConditionalRequestProviderTests {

    private class Controller { }

    private static ExecutionRequestHandlerInfo Handler(string method) =>
        new("/rates", method, typeof(Controller), "Read");

    private static IRequestFilterProvider Installed() {
        var services = new ServiceCollection();

        new HardenedWebModule().ConfigureServices(services);

        return Assert.Single(services.BuildServiceProvider().GetServices<IRequestFilterProvider>());
    }

    /// <summary>
    /// A HEAD reaches the GET handler through the routing table, so a handler declared for GET is
    /// the one a HEAD is revalidated at.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("get")]
    [InlineData("HEAD")]
    public void TheWebModuleInstallsTheFilterAtTheConditionalStageOfEveryReadHandler(string method) {
        var info = Assert.Single(Installed().GetFilters(Handler(method)));

        Assert.Equal(FilterOrder.Conditional, info.Order);
        Assert.IsType<ConditionalRequestFilter>(info.FilterFunc(null!));
    }

    /// <summary>
    /// The conditionals mean a 412 on a write, which is not what this filter answers, so a write
    /// handler carries none of it.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void AWriteHandlerGetsNoFilter(string method) {
        Assert.Empty(Installed().GetFilters(Handler(method)));
    }

    /// <summary>One instance for the application, since the filter holds nothing.</summary>
    [Fact]
    public void OneInstanceServesEveryHandler() {
        var provider = Installed();

        var first = Assert.Single(provider.GetFilters(Handler("GET"))).FilterFunc(null!);
        var second = Assert.Single(provider.GetFilters(Handler("GET"))).FilterFunc(null!);

        Assert.Same(first, second);
    }

    /// <summary>
    /// An application composing two web modules loads this one twice. A second copy would be a
    /// second filter on every GET, standing down at run time for nothing.
    /// </summary>
    [Fact]
    public void TheModuleInstallsOneCopyHoweverOftenItIsApplied() {
        var services = new ServiceCollection();

        new HardenedWebModule().ConfigureServices(services);
        new HardenedWebModule().ConfigureServices(services);

        Assert.Single(services.BuildServiceProvider().GetServices<IRequestFilterProvider>());
    }
}
