using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// A filter short-circuits by returning without calling <c>Next</c>. That is the only
/// mechanism the pipeline offers for refusing a request - an authorization filter that
/// rejects, a cache filter that already has the answer, a rate limiter that says no - so
/// what happens after the return matters as much as the return itself.
/// </summary>
public class ShortCircuitTests {

    private class Controller { }

    /// <summary>
    /// Everything ordered after the short-circuiting filter is skipped, including the
    /// handler.
    /// </summary>
    [Fact]
    public async Task AFilterThatDoesNotCallNextStopsEverythingAfterIt() {
        var log = new List<string>();
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Recording(log, "before"),
            new Pipeline.ShortCircuiting(log, "gate"),
            new Pipeline.Recording(log, "after"),
            new Pipeline.Recording(log, "further-after"));

        await chain.Next();

        Assert.Equal(new[] { "before", "gate" }, log);
    }

    /// <summary>
    /// Filters ahead of the short circuit still get to run their code after their own
    /// <c>Next</c> returns. A metrics or logging filter wrapping the chain must still record
    /// a request that was refused.
    /// </summary>
    [Fact]
    public async Task FiltersWrappingAShortCircuitStillCompleteTheirOwnWork() {
        var log = new List<string>();
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Inline(async c => {
                log.Add("wrapper-enter");

                await c.Next();

                log.Add("wrapper-exit");
            }),
            new Pipeline.ShortCircuiting(log, "gate"),
            new Pipeline.Recording(log, "handler"));

        await chain.Next();

        Assert.Equal(new[] { "wrapper-enter", "gate", "wrapper-exit" }, log);
    }

    /// <summary>
    /// A short circuit that sets a status leaves it set - nothing downstream overwrites it,
    /// because nothing downstream runs. This is what makes "403 and stop" expressible.
    /// </summary>
    [Fact]
    public async Task AShortCircuitedResponseKeepsTheStatusTheFilterSet() {
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.Inline(c => {
                c.Context.Response.Status = 403;
                c.Context.Response.ResponseValue = "forbidden";

                return Task.CompletedTask;
            }),
            new Pipeline.Inline(c => {
                c.Context.Response.Status = 200;

                return Task.CompletedTask;
            }));

        await chain.Next();

        Assert.Equal(403, context.Response.Status);
        Assert.Equal("forbidden", context.Response.ResponseValue);
    }

    /// <summary>
    /// The chain does not consider itself finished just because a filter refused to continue.
    /// <c>IsLastFilter</c> reports the position, so a short circuit leaves filters unreached
    /// and the flag false.
    /// </summary>
    [Fact]
    public async Task AShortCircuitLeavesTheChainShortOfItsLastFilter() {
        var context = Pipeline.Context();

        var chain = Pipeline.Chain(context,
            new Pipeline.ShortCircuiting(new List<string>(), "gate"),
            new Pipeline.Recording(new List<string>(), "never"));

        await chain.Next();

        Assert.False(chain.IsLastFilter);
    }

    /// <summary>
    /// A short circuit ahead of the IO filter skips serialization too, so a filter that has
    /// already written the response is not overwritten by the pipeline's own serializer.
    /// </summary>
    [Fact]
    public async Task AShortCircuitBeforeTheIoFilterSkipsSerializationAndTheHandler() {
        var log = new List<string>();

        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new Pipeline.Recording(log, "io"));

        var instanceProvider = Substitute.For<IInstanceFilterProvider>();
        instanceProvider.ProvideFilter<Controller>(Arg.Any<IServiceProvider>())
            .Returns(new Pipeline.Recording(log, "instance"));

        var registry = new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>());
        registry.RegisterFilter(new Pipeline.ShortCircuiting(log, "gate"),
            (int)ExecutionFilterOrder.Init);

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IGlobalFilterRegistry>(registry);
            services.AddSingleton(ioProvider);
            services.AddSingleton(instanceProvider);
        });

        context.HandlerInstance = new Controller();

        var filters = ExecutionHelper.StandardFilterEmptyParameters<Controller>(
            context.RequestServices,
            new ExecutionRequestHandlerInfo("/orders", "GET", typeof(Controller), "Get"),
            (_, _) => log.Add("invoke"),
            Array.Empty<IRequestFilterProvider>());

        await new ExecutionChain(filters, context).Next();

        Assert.Equal(new[] { "gate" }, log);
    }
}
