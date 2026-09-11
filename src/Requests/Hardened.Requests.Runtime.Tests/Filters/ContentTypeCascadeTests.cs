using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// Where a handler's media types come from, nearest first.
/// </summary>
/// <remarks>
/// <para>
/// Four rungs, and only the bottom two are resolved here. The generator reads a method's
/// <c>[Produces]</c> ahead of its class's and stamps the winner into the handler info, because that
/// is where the syntax is and because the document needs the same answer at build. An
/// <c>[assembly: Produces]</c> and a registered <see cref="ResponseContentTypeDefault"/> only exist
/// once the container does, and <c>ExecutionHelper</c> amends them on as the chain is composed.
/// </para>
/// <para>
/// Asserted on the composed handler rather than on a response, because that is the seam: everything
/// downstream - the binding, the locator, the raw writer - reads
/// <see cref="IExecutionRequestHandlerInfo.ProducedContentTypes"/> and has no other source.
/// </para>
/// </remarks>
public class ContentTypeCascadeTests {

    private class Controller { }

    private sealed class IoStandIn : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    private sealed class InstanceStandIn : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    /// <summary>
    /// Composes the real filter array and hands back the handler as it ended up.
    /// </summary>
    private static IReadOnlyList<string> Compose(
        IReadOnlyList<string>? declared = null,
        Action<ServiceCollection>? configureServices = null) {
        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new IoStandIn());

        var instanceProvider = Substitute.For<IInstanceFilterProvider>();
        instanceProvider.ProvideFilter<Controller>(Arg.Any<IServiceProvider>())
            .Returns(new InstanceStandIn());

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IGlobalFilterRegistry>(
                new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>()));
            services.AddSingleton(ioProvider);
            services.AddSingleton(instanceProvider);

            configureServices?.Invoke(services);
        });

        var handlerInfo = new ExecutionRequestHandlerInfo(
            "/orders", "GET", typeof(Controller), "Read", producedContentTypes: declared);

        return ExecutionHelper.StandardFilterEmptyParameters<Controller>(
                context.RequestServices, handlerInfo, (_, _) => { }, [])
            .HandlerInfo.ProducedContentTypes;
    }

    /// <summary>
    /// Nothing anywhere is an empty set, which is what makes an unannotated handler take the
    /// service's default serializer rather than declare that it produces nothing.
    /// </summary>
    [Fact]
    public void NothingDeclaredAnywhereIsEmpty() {
        Assert.Empty(Compose());
    }

    /// <summary>
    /// The operation's own declaration is the answer, whatever is registered below it.
    /// </summary>
    [Fact]
    public void TheOperationBeatsTheRegisteredDefault() {
        var resolved = Compose(
            declared: new[] { "text/csv" },
            configureServices: services => services.AddSingleton(
                new ResponseContentTypeDefault("application/vnd.msgpack")));

        Assert.Equal(new[] { "text/csv" }, resolved);
    }

    /// <summary>
    /// A registered default reaches a handler that declared nothing, which is how a serializer
    /// package makes itself the default for a whole service.
    /// </summary>
    [Fact]
    public void TheRegisteredDefaultReachesAHandlerThatDeclaredNothing() {
        var resolved = Compose(
            configureServices: services => services.AddSingleton(
                new ResponseContentTypeDefault("application/vnd.msgpack")));

        Assert.Equal(new[] { "application/vnd.msgpack" }, resolved);
    }

    /// <summary>
    /// Two registrations are an application saying two things, and the last one wins - the rule
    /// serializers themselves follow.
    /// </summary>
    [Fact]
    public void TheLastRegisteredDefaultWins() {
        var resolved = Compose(
            configureServices: services => {
                services.AddSingleton(new ResponseContentTypeDefault("application/xml"));
                services.AddSingleton(new ResponseContentTypeDefault("application/vnd.msgpack"));
            });

        Assert.Equal(new[] { "application/vnd.msgpack" }, resolved);
    }

    /// <summary>
    /// An assembly that declares nothing falls through to the registered default rather than to an
    /// empty set.
    /// </summary>
    /// <remarks>
    /// <b>The positive case is not tested here and cannot be.</b> The rung is the handler's own
    /// assembly, so asserting that <c>[assembly: Produces]</c> wins means putting the attribute on
    /// this test assembly - which is the assembly every handler in every other test here belongs
    /// to, so it would change what they all declare. The lookup itself is
    /// <c>Assembly.GetCustomAttributes</c>, the same call <c>TimeoutResolver.ForAssembly</c> makes
    /// for the same rung.
    /// </remarks>
    [Fact]
    public void AnAssemblyThatDeclaresNothingFallsThrough() {
        Assert.Empty(Compose());
    }
}
