using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.RateLimiting;

/// <summary>
/// The in-process store, and the seam that lets an application replace it.
/// </summary>
public class RateLimitStoreTests {

    private static InProcessRateLimitStore Store(RateLimitConfiguration? config = null) =>
        new(config ?? new RateLimitConfiguration());

    private static readonly RateLimitPolicy Small =
        new(PermitLimit: 3, Window: TimeSpan.FromMinutes(5));

    [Fact]
    public async Task Acquire_AllowsUpToTheLimit() {
        var store = Store();

        for (var i = 0; i < 3; i++) {
            var decision = await store.Acquire("caller", Small, CancellationToken.None);

            Assert.True(decision.Allowed);
        }
    }

    [Fact]
    public async Task Acquire_RefusesPastTheLimit() {
        var store = Store();

        for (var i = 0; i < 3; i++) {
            await store.Acquire("caller", Small, CancellationToken.None);
        }

        var refused = await store.Acquire("caller", Small, CancellationToken.None);

        Assert.False(refused.Allowed);
        Assert.Equal(3, refused.Limit);
        Assert.True(refused.RetryAfter > TimeSpan.Zero);
    }

    /// <summary>One caller exhausting its allowance does not refuse anybody else.</summary>
    [Fact]
    public async Task Acquire_CountsEachPartitionSeparately() {
        var store = Store();

        for (var i = 0; i < 3; i++) {
            await store.Acquire("noisy", Small, CancellationToken.None);
        }

        Assert.False((await store.Acquire("noisy", Small, CancellationToken.None)).Allowed);
        Assert.True((await store.Acquire("quiet", Small, CancellationToken.None)).Allowed);
    }

    /// <summary>
    /// Two named policies on one partition are two allowances - a burst limit and an hourly limit
    /// on the same caller must not spend each other's permits.
    /// </summary>
    [Fact]
    public async Task Acquire_CountsEachNamedPolicySeparately() {
        var store = Store();

        var burst = new RateLimitPolicy(3, TimeSpan.FromMinutes(5), "burst");
        var hourly = new RateLimitPolicy(3, TimeSpan.FromMinutes(5), "hourly");

        for (var i = 0; i < 3; i++) {
            await store.Acquire("caller", burst, CancellationToken.None);
        }

        Assert.False((await store.Acquire("caller", burst, CancellationToken.None)).Allowed);
        Assert.True((await store.Acquire("caller", hourly, CancellationToken.None)).Allowed);
    }

    /// <summary>
    /// At the partition cap the store allows rather than refuses. Refusing would let anyone who can
    /// mint partition keys deny service to everyone by filling the table, turning a memory bound
    /// into an outage.
    /// </summary>
    [Fact]
    public async Task Acquire_FailsOpenOnceThePartitionCapIsReached() {
        var store = Store(new RateLimitConfiguration { MaxTrackedPartitions = 2 });

        // Fill the table.
        await store.Acquire("a", Small, CancellationToken.None);
        await store.Acquire("b", Small, CancellationToken.None);

        // A partition beyond the cap is allowed however many times it asks.
        for (var i = 0; i < 10; i++) {
            Assert.True((await store.Acquire("c", Small, CancellationToken.None)).Allowed);
        }
    }

    [Fact]
    public async Task Acquire_ReportsWhatIsLeftOfTheAllowance() {
        var store = Store();

        var first = await store.Acquire("caller", Small, CancellationToken.None);

        Assert.True(first.Remaining < first.Limit);
    }

    // ------------------------------------------------------------ the seam

    /// <summary>
    /// The framework's store is registered with <c>Try</c>, so an application that registers its
    /// own wins - which is the entire mechanism for pointing this at Redis or DynamoDB without a
    /// framework change.
    /// </summary>
    [Fact]
    public void TheDefaultStoreIsRegisteredSoAnApplicationCanReplaceIt() {
        var attribute = (SingletonServiceAttribute)Attribute.GetCustomAttribute(
            typeof(InProcessRateLimitStore), typeof(SingletonServiceAttribute))!;

        Assert.Equal(RegistrationType.Try, attribute.Using);
    }

    /// <summary>
    /// What replacing it looks like from the application side, both ways round, because module load
    /// order is not something an application controls.
    /// </summary>
    [Fact]
    public void AnApplicationsOwnStoreWinsWhicheverOrderTheModulesLoadIn() {
        var frameworkFirst = new ServiceCollection();

        frameworkFirst.TryAddSingleton<IRateLimitStore, InProcessRateLimitStore>();
        frameworkFirst.AddSingleton<IRateLimitStore, ReplacementStore>();

        var applicationFirst = new ServiceCollection();

        applicationFirst.AddSingleton<IRateLimitStore, ReplacementStore>();
        applicationFirst.TryAddSingleton<IRateLimitStore, InProcessRateLimitStore>();

        foreach (var services in new[] { frameworkFirst, applicationFirst }) {
            services.TryAddSingleton(new RateLimitConfiguration());

            Assert.IsType<ReplacementStore>(
                services.BuildServiceProvider().GetRequiredService<IRateLimitStore>());
        }
    }

    /// <summary>
    /// <c>Replace</c> is the spelling to document, because plain <c>Add</c> leaves the framework's
    /// descriptor registered as well - harmless for a singly-resolved store, but it means the
    /// default is still constructed by anything that enumerates.
    /// </summary>
    [Fact]
    public void ReplaceLeavesExactlyOneRegistration() {
        var services = new ServiceCollection();

        services.TryAddSingleton<IRateLimitStore, InProcessRateLimitStore>();
        services.Replace(ServiceDescriptor.Singleton<IRateLimitStore, ReplacementStore>());
        services.TryAddSingleton(new RateLimitConfiguration());

        var all = services.BuildServiceProvider().GetServices<IRateLimitStore>().ToArray();

        Assert.Single(all);
        Assert.IsType<ReplacementStore>(all[0]);
    }

    private sealed class ReplacementStore : IRateLimitStore {
        public ValueTask<RateLimitDecision> Acquire(
            string partition, RateLimitPolicy policy, CancellationToken cancellationToken) =>
            new(RateLimitDecision.Allow(policy.PermitLimit, policy.PermitLimit));
    }

    // ----------------------------------------------------------- partitioner

    /// <summary>An authenticated caller is counted as themselves.</summary>
    [Fact]
    public void Partition_UsesTheAuthenticatedSubject() {
        var principal = Substitute.For<ICallerPrincipal>();

        principal.IsAuthenticated.Returns(true);
        principal.Subject.Returns("user-7");

        var context = Pipeline.Context();

        context.CallerPrincipal = principal;

        Assert.Equal(
            "sub:user-7",
            new DefaultRateLimitPartitioner(new RateLimitConfiguration()).Partition(context));
    }

    /// <summary>
    /// An unauthenticated caller is counted by a configured header, so a proxy that identifies
    /// callers can partition them without this needing a remote address it does not have.
    /// </summary>
    [Fact]
    public void Partition_FallsBackToTheConfiguredHeader() {
        var context = Pipeline.Context();

        context.Request.Headers["X-Api-Key"] = "key-abc";

        var partitioner = new DefaultRateLimitPartitioner(
            new RateLimitConfiguration { PartitionHeader = "X-Api-Key" });

        Assert.Equal("X-Api-Key:key-abc", partitioner.Partition(context));
    }

    /// <summary>
    /// A request that cannot be attributed to anyone counts against one shared allowance rather
    /// than getting one of its own. A partition per anonymous caller is not a limit, it is a memory
    /// leak wearing a limiter's name.
    /// </summary>
    [Fact]
    public void Partition_PutsEveryUnattributableRequestInOneBucket() {
        var partitioner = new DefaultRateLimitPartitioner(new RateLimitConfiguration());

        Assert.Equal(
            DefaultRateLimitPartitioner.Anonymous, partitioner.Partition(Pipeline.Context()));
        Assert.Equal(
            DefaultRateLimitPartitioner.Anonymous, partitioner.Partition(Pipeline.Context()));
    }

    /// <summary>
    /// A configured header that the request does not carry is not a partition - it falls back to
    /// the shared bucket rather than to an empty key that every such caller would share silently.
    /// </summary>
    [Fact]
    public void Partition_FallsBackWhenTheConfiguredHeaderIsAbsent() {
        var partitioner = new DefaultRateLimitPartitioner(
            new RateLimitConfiguration { PartitionHeader = "X-Api-Key" });

        Assert.Equal(
            DefaultRateLimitPartitioner.Anonymous, partitioner.Partition(Pipeline.Context()));
    }
}
