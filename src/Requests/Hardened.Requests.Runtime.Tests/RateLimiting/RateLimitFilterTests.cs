using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.RateLimiting;

/// <summary>
/// What the filter does with a store's verdict, and - more importantly - how it refuses.
///
/// <para>
/// The filter runs on both sides of <c>FilterOrder.Serialization</c> depending on what it is
/// keyed on, and the two sides need different refusals. Returning without calling <c>Next</c>
/// ahead of that filter produces a 429 with an empty body, because the thing that would have
/// written one never runs. Most of what is asserted here is that distinction.
/// </para>
/// </summary>
public class RateLimitFilterTests {

    private sealed class FixedStore : IRateLimitStore {
        private readonly RateLimitDecision _decision;

        public int Calls { get; private set; }

        public string? LastPartition { get; private set; }

        public RateLimitPolicy LastPolicy { get; private set; }

        public FixedStore(RateLimitDecision decision) {
            _decision = decision;
        }

        public ValueTask<RateLimitDecision> Acquire(
            string partition, RateLimitPolicy policy, CancellationToken cancellationToken) {
            Calls++;
            LastPartition = partition;
            LastPolicy = policy;

            return new ValueTask<RateLimitDecision>(_decision);
        }
    }

    private sealed class FixedPartitioner : IRateLimitPartitioner {
        private readonly string _partition;

        public FixedPartitioner(string partition) {
            _partition = partition;
        }

        public string Partition(IExecutionContext context) => _partition;
    }

    private static IExecutionContext Context(
        IRateLimitStore? store, IRateLimitPartitioner? partitioner = null) =>
        Pipeline.Context(configureServices: services => {
            if (store != null) {
                services.AddSingleton(store);
            }

            services.AddSingleton(partitioner ?? new FixedPartitioner("caller-1"));
        });

    private static readonly RateLimitPolicy Policy =
        new(PermitLimit: 10, Window: TimeSpan.FromMinutes(1));

    [Fact]
    public async Task Execute_LetsAnAllowedRequestThrough() {
        var store = new FixedStore(RateLimitDecision.Allow(10, 9));
        var context = Context(store);
        var reached = false;

        await Pipeline.Chain(
            context,
            new RateLimitFilter(Policy, beforeSerialization: false),
            new Pipeline.Inline(_ => {
                reached = true;

                return Task.CompletedTask;
            })).Next();

        Assert.True(reached);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>
    /// A client is told its allowance before it runs out, so it can pace itself rather than
    /// discover the limit by being refused.
    /// </summary>
    [Fact]
    public async Task Execute_ReportsTheAllowanceOnAnAllowedRequest() {
        var context = Context(new FixedStore(RateLimitDecision.Allow(10, 7)));

        await Pipeline.Chain(
            context,
            new RateLimitFilter(Policy, beforeSerialization: false),
            new Pipeline.Inline(_ => Task.CompletedTask)).Next();

        Assert.Equal("10", context.Response.Headers["RateLimit-Limit"].ToString());
        Assert.Equal("7", context.Response.Headers["RateLimit-Remaining"].ToString());
    }

    /// <summary>Behind the serialization filter, refusing means not continuing.</summary>
    [Fact]
    public async Task Execute_StopsTheChainWhenRefusingBehindSerialization() {
        var context = Context(new FixedStore(RateLimitDecision.Refuse(10, TimeSpan.FromSeconds(30))));
        var reached = false;

        await Pipeline.Chain(
            context,
            new RateLimitFilter(Policy, beforeSerialization: false),
            new Pipeline.Inline(_ => {
                reached = true;

                return Task.CompletedTask;
            })).Next();

        Assert.False(reached);
        Assert.IsType<RateLimitExceededException>(context.Response.ExceptionValue);
    }

    /// <summary>
    /// Ahead of the serialization filter, refusing means recording the failure and continuing -
    /// otherwise nothing downstream runs to turn it into a body, and the caller gets a 429 with
    /// nothing in it.
    /// </summary>
    [Fact]
    public async Task Execute_ContinuesTheChainWhenRefusingAheadOfSerialization() {
        var context = Context(new FixedStore(RateLimitDecision.Refuse(10, TimeSpan.FromSeconds(30))));
        var reached = false;

        await Pipeline.Chain(
            context,
            new RateLimitFilter(Policy, beforeSerialization: true),
            new Pipeline.Inline(_ => {
                reached = true;

                return Task.CompletedTask;
            })).Next();

        Assert.True(reached);
        Assert.IsType<RateLimitExceededException>(context.Response.ExceptionValue);
    }

    /// <summary>
    /// A limiter with no store configured is not a limiter, and must not be a wall. Failing closed
    /// here would take an application down on a registration mistake.
    /// </summary>
    [Fact]
    public async Task Execute_PassesThroughWhenNoStoreIsRegistered() {
        var context = Context(store: null);
        var reached = false;

        await Pipeline.Chain(
            context,
            new RateLimitFilter(Policy, beforeSerialization: false),
            new Pipeline.Inline(_ => {
                reached = true;

                return Task.CompletedTask;
            })).Next();

        Assert.True(reached);
        Assert.Null(context.Response.ExceptionValue);
    }

    [Fact]
    public async Task Execute_AsksTheStoreAboutThePartitionerSPartition() {
        var store = new FixedStore(RateLimitDecision.Allow(10, 9));
        var context = Context(store, new FixedPartitioner("tenant-42"));

        await Pipeline.Chain(
            context,
            new RateLimitFilter(Policy, beforeSerialization: false),
            new Pipeline.Inline(_ => Task.CompletedTask)).Next();

        Assert.Equal(1, store.Calls);
        Assert.Equal("tenant-42", store.LastPartition);
        Assert.Equal(10, store.LastPolicy.PermitLimit);
    }

    // ------------------------------------------------------------- the 429

    /// <summary>
    /// A 429 is not well-formed without <c>Retry-After</c>, which is why the refusal is an
    /// <see cref="IStatusCodeException"/> - the pipeline asks it for its headers before writing.
    /// </summary>
    [Fact]
    public void RateLimitExceededException_CarriesRetryAfterAndTheAllowance() {
        var exception = new RateLimitExceededException(
            RateLimitDecision.Refuse(10, TimeSpan.FromSeconds(30)));

        var headers = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();

        exception.ApplyHeaders(headers);

        Assert.Equal(429, exception.StatusCode);
        Assert.Equal("30", headers[KnownHeaders.RetryAfter].ToString());
        Assert.Equal("10", headers["RateLimit-Limit"].ToString());
        Assert.Equal("0", headers["RateLimit-Remaining"].ToString());
    }

    /// <summary>
    /// Rounded up, and never to zero. Rounding down invites the caller back a moment before the
    /// allowance exists, which produces a second 429 and a client that believes the header lies.
    /// </summary>
    [Theory]
    [InlineData(0.1, "1")]
    [InlineData(1.2, "2")]
    [InlineData(30.0, "30")]
    public void RateLimitExceededException_RoundsRetryAfterUp(double seconds, string expected) {
        var exception = new RateLimitExceededException(
            RateLimitDecision.Refuse(10, TimeSpan.FromSeconds(seconds)));

        var headers = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();

        exception.ApplyHeaders(headers);

        Assert.Equal(expected, headers[KnownHeaders.RetryAfter].ToString());
    }

    // ---------------------------------------------------------- the attribute

    /// <summary>
    /// A transport-keyed limit runs ahead of authentication, and therefore ahead of serialization -
    /// so it refuses without the request body having been read.
    /// </summary>
    [Fact]
    public void RateLimitAttribute_OrdersATransportLimitAheadOfAuthenticationAndSerialization() {
        var info = Assert.Single(
            new RateLimitAttribute { Scope = RateLimitScope.Transport }.GetFilters(null!));

        Assert.Equal(FilterOrder.RateLimitTransport, info.Order);
        Assert.True(info.Order < FilterOrder.Authentication);
        Assert.True(info.Order < FilterOrder.Serialization);
    }

    /// <summary>A principal-keyed limit runs after authentication, because it needs a caller.</summary>
    [Fact]
    public void RateLimitAttribute_OrdersAPrincipalLimitAfterAuthentication() {
        var info = Assert.Single(
            new RateLimitAttribute { Scope = RateLimitScope.Principal }.GetFilters(null!));

        Assert.Equal(FilterOrder.RateLimitPrincipal, info.Order);
        Assert.True(info.Order > FilterOrder.Authentication);
    }

    /// <summary>
    /// Neither slot collides with another filter's order. A tie sorts unpredictably, because the
    /// filter array is sorted with an unstable sort - so two filters at one order have no defined
    /// relative position, and for anything straddling serialization that decides whether a body is
    /// written.
    /// </summary>
    [Fact]
    public void RateLimitAttribute_UsesOrdersNothingElseClaims() {
        var taken = new[] {
            FilterOrder.HandlerCreation, FilterOrder.Authentication, FilterOrder.BeforeSerialization,
            FilterOrder.Serialization, FilterOrder.Validation, FilterOrder.Authorization,
            FilterOrder.Retry, FilterOrder.DefaultValue, FilterOrder.EndPointInvoke
        };

        Assert.DoesNotContain(FilterOrder.RateLimitTransport, taken);
        Assert.DoesNotContain(FilterOrder.RateLimitPrincipal, taken);
    }

    /// <summary>
    /// Two limits on one handler - a burst limit and an hourly one - are named apart so they do not
    /// share a counter.
    /// </summary>
    [Fact]
    public void RateLimitAttribute_AllowsMoreThanOnePerHandler() {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(RateLimitAttribute), typeof(AttributeUsageAttribute))!;

        Assert.True(usage.AllowMultiple);
    }

    [Fact]
    public void RateLimitAttribute_PassesItsConfigurationToThePolicy() {
        var attribute = new RateLimitAttribute {
            PermitLimit = 5, WindowSeconds = 30, Name = "burst"
        };

        var store = new FixedStore(RateLimitDecision.Allow(5, 4));
        var context = Context(store);

        var filter = Assert.Single(attribute.GetFilters(null!)).FilterFunc(context);

        filter.Execute(Pipeline.Chain(context, filter)).GetAwaiter().GetResult();

        Assert.Equal(5, store.LastPolicy.PermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), store.LastPolicy.Window);
        Assert.Equal("burst", store.LastPolicy.Name);
    }
}
