using Hardened.Gcp.CloudRun.Runtime.Dispatch;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Dispatch;

/// <summary>
/// The composition over a hand-built collection, and the routing of the composite itself.
/// </summary>
public class CloudRunDispatchTests {

    /// <summary>
    /// What a collection looks like after <c>[KestrelRuntime]</c> and the function generator have
    /// both registered: the routing table under its interface and as a dispatch, and the
    /// function dispatch by type.
    /// </summary>
    private static ServiceCollection BothFamilies() {
        var services = new ServiceCollection();

        services.AddSingleton<IWebExecutionHandlerService, StubRouting>();
        services.AddSingleton<IHandlerDispatch>(provider => provider.GetRequiredService<IWebExecutionHandlerService>());
        services.AddSingleton<IHandlerDispatch, FunctionDispatchFilter>();

        return services;
    }

    [Fact]
    public void TwoDispatchesBecomeOneComposite() {
        var services = BothFamilies();

        CloudRunDispatch.Compose(services);

        var provider = services.BuildServiceProvider();

        var dispatch = Assert.Single(provider.GetServices<IHandlerDispatch>());
        var composed = Assert.IsType<CloudRunDispatch>(dispatch);

        Assert.IsType<StubRouting>(composed.Web);
        Assert.IsType<FunctionDispatchFilter>(composed.Function);
    }

    /// <summary>Kestrel resolves the routing table by its own interface, so the composite answers there too.</summary>
    [Fact]
    public void TheCompositeIsTheRoutingTableKestrelResolves() {
        var services = BothFamilies();

        CloudRunDispatch.Compose(services);

        var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IWebExecutionHandlerService>(),
            provider.GetRequiredService<IHandlerDispatch>());
    }

    [Fact]
    public void AWebOnlyCollectionIsLeftAlone() {
        var services = new ServiceCollection();

        services.AddSingleton<IWebExecutionHandlerService, StubRouting>();
        services.AddSingleton<IHandlerDispatch>(provider => provider.GetRequiredService<IWebExecutionHandlerService>());

        CloudRunDispatch.Compose(services);

        var provider = services.BuildServiceProvider();

        Assert.IsType<StubRouting>(Assert.Single(provider.GetServices<IHandlerDispatch>()));
    }

    [Fact]
    public void ComposingTwiceComposesOnce() {
        var services = BothFamilies();

        CloudRunDispatch.Compose(services);
        CloudRunDispatch.Compose(services);

        var provider = services.BuildServiceProvider();

        var composed = Assert.IsType<CloudRunDispatch>(Assert.Single(provider.GetServices<IHandlerDispatch>()));

        Assert.IsType<StubRouting>(composed.Web);
    }

    [Theory]
    [InlineData("QUEUE")]
    [InlineData("TOPIC")]
    [InlineData("TIMER")]
    [InlineData("EVENT")]
    [InlineData("CHANGE")]
    [InlineData("STREAM")]
    [InlineData("BLOB")]
    [InlineData("INVOKE")]
    public async Task ATriggerSchemeRoutesThroughTheFunctionTable(string scheme) {
        var (web, function, chain) = Composed(scheme);

        await new CloudRunDispatch(web, function).Execute(chain);

        await function.Received(1).Execute(chain);
        await web.DidNotReceive().Execute(chain);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("HEAD")]
    [InlineData("queue")]
    public async Task AnythingElseRoutesThroughTheWebTable(string method) {
        var (web, function, chain) = Composed(method);

        await new CloudRunDispatch(web, function).Execute(chain);

        await web.Received(1).Execute(chain);
        await function.DidNotReceive().Execute(chain);
    }

    /// <summary>An application that compiled no trigger handlers has no function table, and a trigger scheme is the web table's 404.</summary>
    [Fact]
    public async Task WithoutAFunctionTableATriggerSchemeRoutesThroughTheWebTable() {
        var (web, _, chain) = Composed("QUEUE");

        await new CloudRunDispatch(web, null).Execute(chain);

        await web.Received(1).Execute(chain);
    }

    private static (IHandlerDispatch Web, IHandlerDispatch Function, IExecutionChain Chain) Composed(string method) {
        var request = Substitute.For<IExecutionRequest>();
        request.Method.Returns(method);

        var context = Substitute.For<IExecutionContext>();
        context.Request.Returns(request);

        var chain = Substitute.For<IExecutionChain>();
        chain.Context.Returns(context);

        return (Substitute.For<IHandlerDispatch>(), Substitute.For<IHandlerDispatch>(), chain);
    }

    private sealed class StubRouting : IWebExecutionHandlerService {
        public Task Execute(IExecutionChain chain) => Task.CompletedTask;
    }
}
