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
/// The order filters run in is the pipeline's whole contract. A retry filter that runs after
/// the body has been deserialized cannot replay it; an authorization filter that runs after
/// the handler has already been invoked authorizes nothing.
///
/// <para>
/// Ordering is decided in one place - <c>ExecutionHelper.CreateFilterArray</c> - which sorts
/// every filter by <see cref="RequestFilterInfo.Order"/> and pins the three pipeline filters
/// at fixed positions: the instance filter at <see cref="FilterOrder.HandlerCreation"/>, the
/// IO filter at <see cref="FilterOrder.Serialization"/> and the invoke filter at
/// <see cref="FilterOrder.EndPointInvoke"/>. These tests assert the observable outcome: the
/// sequence filters actually execute in.
/// </para>
/// </summary>
public class FilterOrderingTests {

    private const string Instance = "instance";
    private const string Io = "io";
    private const string Invoke = "invoke";

    private class Controller { }

    /// <summary>
    /// Builds the real filter array through <c>ExecutionHelper</c> and runs it, returning the
    /// names in the order they executed. The three pipeline filters record themselves under
    /// <see cref="Instance"/>, <see cref="Io"/> and <see cref="Invoke"/>.
    /// </summary>
    private static async Task<List<string>> Run(
        List<string> log,
        Action<IGlobalFilterRegistry> register,
        params IRequestFilterProvider[] filterProviders) {

        var registry = new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>());

        register(registry);

        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new Pipeline.Recording(log, Io));

        var instanceProvider = Substitute.For<IInstanceFilterProvider>();
        instanceProvider.ProvideFilter<Controller>(Arg.Any<IServiceProvider>())
            .Returns(new Pipeline.Recording(log, Instance));

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IGlobalFilterRegistry>(registry);
            services.AddSingleton(ioProvider);
            services.AddSingleton(instanceProvider);
        });

        context.HandlerInstance = new Controller();

        var handlerInfo = new ExecutionRequestHandlerInfo(
            "/orders", "GET", typeof(Controller), nameof(Run));

        var filters = ExecutionHelper.StandardFilterEmptyParameters<Controller>(
            context.RequestServices,
            handlerInfo,
            (_, _) => log.Add(Invoke),
            filterProviders);

        await new ExecutionChain(filters, context).Next();

        return log;
    }

    private static IRequestFilterProvider AtOrder(List<string> log, string name, int? order) {
        var provider = Substitute.For<IRequestFilterProvider>();

        provider.GetFilters(Arg.Any<IExecutionRequestHandlerInfo>())
            .Returns(new[] { new RequestFilterInfo(_ => new Pipeline.Recording(log, name), order) });

        return provider;
    }

    /// <summary>
    /// Every value on <see cref="ExecutionFilterOrder"/>, registered at once and in reverse so
    /// the sort has to do the work. The pipeline's own three filters interleave with them at
    /// their documented positions, which is the point: a filter author picks an
    /// <c>ExecutionFilterOrder</c> to land on a particular side of parameter binding and
    /// handler invocation.
    ///
    /// <para>
    /// <see cref="ExecutionFilterOrder.Last"/> is absent from the expected sequence because it
    /// sorts above <see cref="FilterOrder.EndPointInvoke"/>, and the invoke filter is terminal
    /// - see <see cref="AFilterOrderedAfterTheHandlerNeverRuns"/>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheWholeExecutionFilterOrderRangeRunsInAscendingOrder() {
        var log = new List<string>();

        await Run(log, registry => {
            foreach (var value in Enum.GetValues<ExecutionFilterOrder>().OrderByDescending(v => v)) {
                registry.RegisterFilter(new Pipeline.Recording(log, value.ToString()), (int)value);
            }
        });

        Assert.Equal(new[] {
            nameof(ExecutionFilterOrder.Init),               // -10000
            nameof(ExecutionFilterOrder.FullRequestMetrics), //  -7000
            nameof(ExecutionFilterOrder.RetryFilter),        //  -5000
            Instance,                                        //  -1000  FilterOrder.HandlerCreation
            nameof(ExecutionFilterOrder.BeforeSerialize),    //     -1
            nameof(ExecutionFilterOrder.BindParameters),     //      0
            nameof(ExecutionFilterOrder.First),              //      1
            nameof(ExecutionFilterOrder.Second),             //      2
            nameof(ExecutionFilterOrder.Third),              //      3
            Io,                                              //      5  FilterOrder.Serialization
            nameof(ExecutionFilterOrder.Normal),             //    100
            Invoke                                           //   2000  FilterOrder.EndPointInvoke
        }, log);
    }

    /// <summary>
    /// A filter registered before parameter binding sees the request ahead of the IO filter;
    /// one registered after it does not. Table-driven across the range because each value is
    /// a separate branch through the same comparison.
    /// </summary>
    [Theory]
    [InlineData(ExecutionFilterOrder.Init, true)]
    [InlineData(ExecutionFilterOrder.FullRequestMetrics, true)]
    [InlineData(ExecutionFilterOrder.RetryFilter, true)]
    [InlineData(ExecutionFilterOrder.BeforeSerialize, true)]
    [InlineData(ExecutionFilterOrder.BindParameters, true)]
    [InlineData(ExecutionFilterOrder.First, true)]
    [InlineData(ExecutionFilterOrder.Second, true)]
    [InlineData(ExecutionFilterOrder.Third, true)]
    [InlineData(ExecutionFilterOrder.Normal, false)]
    public async Task AFilterRunsBeforeSerializationExactlyWhenItsOrderIsLower(
        ExecutionFilterOrder order, bool expectedBeforeIo) {

        var log = new List<string>();

        var result = await Run(log, registry =>
            registry.RegisterFilter(new Pipeline.Recording(log, "subject"), (int)order));

        Assert.Contains("subject", result);
        Assert.Equal(expectedBeforeIo, result.IndexOf("subject") < result.IndexOf(Io));
    }

    /// <summary>
    /// The invoke filter is terminal - it never calls <c>Next</c> - so a filter sorted above
    /// <see cref="FilterOrder.EndPointInvoke"/> is built into the chain and then silently
    /// never reached. <see cref="ExecutionFilterOrder.Last"/> is <c>int.MaxValue</c> and is
    /// therefore unreachable in a standard handler pipeline, which is worth pinning because
    /// nothing reports it: the filter simply does not run.
    /// </summary>
    [Theory]
    [InlineData(FilterOrder.EndPointInvoke + 1)]
    [InlineData(int.MaxValue)]
    public async Task AFilterOrderedAfterTheHandlerNeverRuns(int order) {
        var log = new List<string>();

        var result = await Run(log, registry =>
            registry.RegisterFilter(new Pipeline.Recording(log, "after-handler"), order));

        Assert.Equal(new[] { Instance, Io, Invoke }, result);
        Assert.DoesNotContain("after-handler", result);
    }

    /// <summary>
    /// A <see cref="RequestFilterInfo"/> with no order is sorted as
    /// <see cref="FilterOrder.DefaultValue"/>, which puts it after everything at
    /// <see cref="ExecutionFilterOrder.Normal"/> and before the handler.
    /// </summary>
    [Fact]
    public async Task AFilterWithNoOrderSortsAtTheDefaultValue() {
        var log = new List<string>();

        var result = await Run(
            log,
            registry => registry.RegisterFilter(
                new Pipeline.Recording(log, "explicit-default"), FilterOrder.DefaultValue),
            AtOrder(log, "no-order", null));

        Assert.Equal(Io, result[1]);
        Assert.Equal(Invoke, result[^1]);

        // Both sit between the IO filter and the handler, whichever way the sort broke the tie.
        Assert.Contains("no-order", result[2..^1]);
        Assert.Contains("explicit-default", result[2..^1]);
    }

    /// <summary>
    /// Filters that tie all run, and all run together - between the filter below them and the
    /// filter above them. Their order relative to each other is not part of the contract:
    /// <c>List&lt;T&gt;.Sort</c> is not a stable sort, so a filter that needs to precede
    /// another must say so with a distinct order rather than relying on registration order.
    /// </summary>
    [Fact]
    public async Task TiedFiltersAllRunTogetherBetweenTheirNeighbours() {
        var log = new List<string>();

        var result = await Run(log, registry => {
            registry.RegisterFilter(new Pipeline.Recording(log, "below"), 10);
            registry.RegisterFilter(new Pipeline.Recording(log, "tie-a"), 50);
            registry.RegisterFilter(new Pipeline.Recording(log, "tie-b"), 50);
            registry.RegisterFilter(new Pipeline.Recording(log, "tie-c"), 50);
            registry.RegisterFilter(new Pipeline.Recording(log, "above"), 90);
        });

        var tied = new[] { "tie-a", "tie-b", "tie-c" };

        Assert.All(tied, name => Assert.Contains(name, result));

        var first = tied.Min(result.IndexOf);
        var last = tied.Max(result.IndexOf);

        Assert.Equal(tied.Length - 1, last - first);
        Assert.True(result.IndexOf("below") < first);
        Assert.True(result.IndexOf("above") > last);
    }

    /// <summary>
    /// Attribute-supplied filters and globally registered filters are sorted together, not
    /// appended as separate groups. <c>[Retry]</c> depends on this: it is an attribute on the
    /// handler and has to beat every globally registered filter that would read the body.
    /// </summary>
    [Fact]
    public async Task AttributeFiltersAndGlobalFiltersShareOneOrdering() {
        var log = new List<string>();

        var result = await Run(
            log,
            registry => {
                registry.RegisterFilter(new Pipeline.Recording(log, "global-early"), -6000);
                registry.RegisterFilter(new Pipeline.Recording(log, "global-late"), 500);
            },
            AtOrder(log, "attribute-earliest", -9000),
            AtOrder(log, "attribute-middle", 200));

        Assert.Equal(new[] {
            "attribute-earliest",
            "global-early",
            Instance,
            Io,
            "attribute-middle",
            "global-late",
            Invoke
        }, result);
    }

    /// <summary>
    /// <c>[Retry]</c> orders itself at <c>FilterOrder.HandlerCreation - 10</c> so that it wraps
    /// controller creation as well as the handler call. A retry that reused one controller
    /// instance across attempts would replay whatever state the failed attempt left behind.
    /// </summary>
    [Fact]
    public async Task RetryIsOrderedAheadOfControllerCreation() {
        var log = new List<string>();

        var result = await Run(log, _ => { }, new RecordingRetryProvider(log));

        Assert.Equal(new[] { "retry", Instance, Io, Invoke }, result);
    }

    /// <summary>
    /// Stands in for <c>[Retry]</c> at exactly the order the real attribute uses, so the test
    /// asserts the position rather than re-testing the retry loop.
    /// </summary>
    private class RecordingRetryProvider : IRequestFilterProvider {
        private readonly List<string> _log;

        public RecordingRetryProvider(List<string> log) {
            _log = log;
        }

        public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
            yield return new RequestFilterInfo(
                _ => new Pipeline.Recording(_log, "retry"), FilterOrder.HandlerCreation - 10);
        }
    }
}
