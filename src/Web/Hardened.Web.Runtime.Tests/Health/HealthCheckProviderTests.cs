using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.Health;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Health;

/// <summary>
/// Liveness and readiness answer different questions, and an orchestrator does different things
/// with the answers: it restarts what fails liveness and drains what fails readiness. Conflating
/// them is how a dependency outage becomes a restart loop across every replica at once, so most of
/// what is asserted here is the separation.
/// </summary>
public class HealthCheckProviderTests {

    private sealed class Stub : IHealthCheck {
        private readonly Func<CancellationToken, Task<HealthCheckResult>> _check;

        public Stub(string name, Func<CancellationToken, Task<HealthCheckResult>> check) {
            Name = name;
            _check = check;
        }

        public Stub(string name, HealthCheckResult result)
            : this(name, _ => Task.FromResult(result)) { }

        public string Name { get; }

        public int Calls { get; private set; }

        public Task<HealthCheckResult> Check(CancellationToken cancellationToken) {
            Calls++;

            return _check(cancellationToken);
        }
    }

    private static (HealthCheckProvider Provider, HealthCheckConfiguration Config,
        ServiceProvider Services) Build(params IHealthCheck[] checks) =>
        Build(new HealthCheckConfiguration(), checks);

    /// <summary>
    /// A pass-through, standing in for the serialization filter.
    /// </summary>
    /// <remarks>
    /// A probe sets <c>ShouldSerialize = false</c> and writes its own body, so the real filter has
    /// nothing to do here - and constructing one would drag a serializer and a content negotiator
    /// into tests about liveness.
    /// </remarks>
    private sealed class PassThrough : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    /// <summary>
    /// The services <c>ExecutionHelper</c> resolves while assembling a chain.
    /// </summary>
    /// <remarks>
    /// The probes build their chains through the helper now, rather than hand-rolling one, which is
    /// what puts conventions and <c>IGlobalFilterRegistry</c> in front of them. The cost here is
    /// that these tests need the container a real application has.
    /// </remarks>
    private static ServiceProvider Services(
        HealthCheckConfiguration config, IHealthCheck[] checks,
        Action<IServiceCollection>? configure = null) {
        var services = new ServiceCollection();

        foreach (var check in checks) {
            services.AddSingleton(check);
        }

        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new PassThrough());

        services.AddSingleton(ioProvider);
        services.AddSingleton<IInstanceFilterProvider, InstanceFilterProvider>();
        services.AddSingleton<IGlobalFilterRegistry>(
            new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>()));
        services.AddSingleton(config);
        services.AddSingleton<HealthCheckController>();

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static (HealthCheckProvider, HealthCheckConfiguration, ServiceProvider) Build(
        HealthCheckConfiguration config, params IHealthCheck[] checks) {
        var services = Services(config, checks);

        // The same container for the provider and the context. The controller is resolved from the
        // context and reads its checks from the container it was constructed with, so two would
        // mean the probe ran a different set of checks from the one the test registered.
        return (new HealthCheckProvider(config, services), config, services);
    }

    private static IExecutionContext Context(
        string path, string method = "GET", IServiceProvider? services = null) {
        var request = new TestExecutionRequest(
            method, path, "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>()));

        services ??= Services(new HealthCheckConfiguration(), Array.Empty<IHealthCheck>());

        return new TestExecutionContext(
            services, services, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(new MemoryStream()), CancellationToken.None);
    }

    private static async Task<(int? Status, JsonElement Body)> Probe(
        HealthCheckProvider provider, IServiceProvider services, string path,
        string method = "GET") {
        var context = Context(path, method, services);
        var match = provider.GetExecutionRequestHandler(context);

        Assert.NotNull(match);
        Assert.NotNull(match!.Handler);

        await match.Handler!.GetExecutionChain(context).Next();

        var json = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

        return (context.Response.Status, JsonDocument.Parse(json).RootElement);
    }

    // ------------------------------------------------------- governable at last

    /// <summary>Requires a grant of everything under a path prefix.</summary>
    private sealed class PrefixConvention : IAuthorizationConvention {
        public Requirement? Apply(IExecutionRequestHandlerInfo handlerInfo) =>
            handlerInfo.Path.StartsWith("/health", StringComparison.Ordinal)
                ? Requirement.Grant("ops:probe")
                : null;
    }

    private static IExecutionRequestHandlerInfo HandlerInfoFor(
        HealthCheckProvider provider, IServiceProvider services, string path) {
        var match = provider.GetExecutionRequestHandler(Context(path, services: services));

        Assert.NotNull(match);

        return match!.Handler!.HandlerInfo;
    }

    /// <summary>
    /// A convention reaches both probes.
    /// </summary>
    /// <remarks>
    /// It did not. Each probe built its own one-filter chain, and <c>IGlobalFilterRegistry</c> -
    /// where <c>AuthorizationFilterProvider</c> lives - is only consulted inside
    /// <c>ExecutionHelper.CreateFilterArray</c>, so neither endpoint was reachable by any
    /// authorization mechanism at all.
    /// </remarks>
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public void AConventionReachesAProbe(string path) {
        var config = new HealthCheckConfiguration();
        var services = Services(config, Array.Empty<IHealthCheck>(),
            collection => collection.AddSingleton<IAuthorizationConvention>(new PrefixConvention()));

        var handlerInfo = HandlerInfoFor(
            new HealthCheckProvider(config, services), services, path);

        Assert.Contains("ops:probe", handlerInfo.Requirement!.RequiredGrants);
    }

    /// <summary>
    /// A deployment that wants its probes guarded states it on the configuration.
    /// </summary>
    [Fact]
    public void ADeclaredRequirementReachesAProbe() {
        var config = new HealthCheckConfiguration { Requirement = Requirement.Grant("ops:probe") };
        var services = Services(config, Array.Empty<IHealthCheck>());

        var handlerInfo = HandlerInfoFor(
            new HealthCheckProvider(config, services), services, "/health/ready");

        Assert.Contains("ops:probe", handlerInfo.Requirement!.RequiredGrants);
    }

    /// <summary>
    /// And the default is unchanged: no requirement, so a probe inherits the application's posture
    /// rather than overriding it. A liveness probe that has to authenticate reports unhealthy when
    /// the identity provider is down, which is the opposite of what it is for.
    /// </summary>
    [Fact]
    public void NothingConfiguredLeavesAProbeUnguarded() {
        var config = new HealthCheckConfiguration();
        var services = Services(config, Array.Empty<IHealthCheck>());

        Assert.Null(
            HandlerInfoFor(new HealthCheckProvider(config, services), services, "/health/live")
                .Requirement);
    }

    // ------------------------------------------------------------- liveness

    /// <summary>
    /// Liveness consults nothing. This is the whole reason it is a separate endpoint: an
    /// orchestrator restarts what fails it, and a liveness probe wired to a dependency restarts
    /// every replica the moment that dependency blinks.
    /// </summary>
    [Fact]
    public async Task Live_AnswersHealthyWithoutRunningAnyCheck() {
        var unhealthy = new Stub("db", HealthCheckResult.Unhealthy("down"));
        var (provider, _, services) = Build(unhealthy);

        var (status, body) = await Probe(provider, services, "/health/live");

        Assert.Equal(200, status);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal(0, unhealthy.Calls);
    }

    // ------------------------------------------------------------ readiness

    /// <summary>
    /// No registered checks is ready. An application that registered none has not said it is
    /// unhealthy, it has said it has nothing to verify.
    /// </summary>
    [Fact]
    public async Task Ready_AnswersHealthyWhenNoChecksAreRegistered() {
        var (provider, _, services) = Build();

        var (status, body) = await Probe(provider, services, "/health/ready");

        Assert.Equal(200, status);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_RunsEveryRegisteredCheck() {
        var first = new Stub("a", HealthCheckResult.Healthy());
        var second = new Stub("b", HealthCheckResult.Healthy());

        var (provider, _, services) = Build(first, second);

        await Probe(provider, services, "/health/ready");

        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    /// <summary>
    /// Degraded is a 200. A warm-but-not-hot cache or a slow replica is not a reason to take an
    /// instance out of the load balancer, and treating it as one drains capacity exactly when the
    /// system is under strain.
    /// </summary>
    [Theory]
    [InlineData(HealthStatus.Healthy, 200)]
    [InlineData(HealthStatus.Degraded, 200)]
    [InlineData(HealthStatus.Unhealthy, 503)]
    public async Task Ready_MapsStatusToCode(HealthStatus status, int expected) {
        var (provider, _, services) = Build(new Stub("only", new HealthCheckResult(status)));

        var (code, body) = await Probe(provider, services, "/health/ready");

        Assert.Equal(expected, code);
        Assert.Equal(status.ToString(), body.GetProperty("status").GetString());
    }

    /// <summary>The worst check decides the overall answer.</summary>
    [Fact]
    public async Task Ready_ReportsTheWorstOfTheChecks() {
        var (provider, _, services) = Build(
            new Stub("fine", HealthCheckResult.Healthy()),
            new Stub("slow", HealthCheckResult.Degraded()),
            new Stub("down", HealthCheckResult.Unhealthy()));

        var (status, body) = await Probe(provider, services, "/health/ready");

        Assert.Equal(503, status);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// A check that throws is unhealthy, not a 500. "I could not reach it" is exactly what a
    /// dependency failure looks like from inside, and a 500 from a readiness endpoint tells an
    /// orchestrator far less than a 503.
    /// </summary>
    [Fact]
    public async Task Ready_TreatsAThrowingCheckAsUnhealthyRatherThanFailing() {
        var (provider, _, services) = Build(
            new Stub("explodes", _ => throw new InvalidOperationException("boom")));

        var (status, body) = await Probe(provider, services, "/health/ready");

        Assert.Equal(503, status);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// A check that overruns its timeout is unhealthy, and the probe still answers. A health
    /// endpoint that hangs is worse than one that fails, because the orchestrator's own probe
    /// timeout decides instead - and it decides to restart.
    /// </summary>
    [Fact]
    public async Task Ready_BoundsACheckThatWouldHang() {
        var config = new HealthCheckConfiguration {
            CheckTimeout = TimeSpan.FromMilliseconds(50),
            TotalTimeout = TimeSpan.FromSeconds(5)
        };

        var (provider, _, services) = Build(
            config,
            new Stub("hangs", async token => {
                await Task.Delay(TimeSpan.FromSeconds(30), token);

                return HealthCheckResult.Healthy();
            }));

        var (status, _) = await Probe(provider, services, "/health/ready");

        Assert.Equal(503, status);
    }

    /// <summary>The check is handed a token that will actually be cancelled.</summary>
    [Fact]
    public async Task Ready_PassesACancellableTokenToTheCheck() {
        var config = new HealthCheckConfiguration { CheckTimeout = TimeSpan.FromMilliseconds(50) };
        var observed = CancellationToken.None;

        var (provider, _, services) = Build(
            config,
            new Stub("watches", token => {
                observed = token;

                return Task.FromResult(HealthCheckResult.Healthy());
            }));

        await Probe(provider, services, "/health/ready");

        Assert.True(observed.CanBeCanceled);
    }

    // --------------------------------------------------------------- detail

    /// <summary>
    /// Status only by default. Readiness is unauthenticated by construction, so a body enumerating
    /// dependency names and error strings hands an anonymous caller a map of the system.
    /// </summary>
    [Fact]
    public async Task Ready_ReportsNoDetailByDefault() {
        var (provider, _, services) = Build(
            new Stub("internal-billing-db", HealthCheckResult.Unhealthy("connection refused")));

        var (_, body) = await Probe(provider, services, "/health/ready");

        Assert.False(body.TryGetProperty("checks", out _));
    }

    [Fact]
    public async Task Ready_ReportsPerCheckDetailWhenAsked() {
        var config = new HealthCheckConfiguration { IncludeDetail = true };

        var (provider, _, services) = Build(
            config,
            new Stub("db", HealthCheckResult.Unhealthy("connection refused")),
            new Stub("cache", HealthCheckResult.Healthy()));

        var (_, body) = await Probe(provider, services, "/health/ready");

        var checks = body.GetProperty("checks");

        Assert.Equal("Unhealthy", checks.GetProperty("db").GetProperty("status").GetString());
        Assert.Equal(
            "connection refused", checks.GetProperty("db").GetProperty("description").GetString());
        Assert.Equal("Healthy", checks.GetProperty("cache").GetProperty("status").GetString());
    }

    // -------------------------------------------------------------- routing

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public void GetExecutionRequestHandler_AnswersBothPaths(string path) {
        var (provider, _, services) = Build();

        Assert.NotNull(provider.GetExecutionRequestHandler(Context(path)));
    }

    /// <summary>A probe issuing HEAD is asking the same question.</summary>
    [Fact]
    public void GetExecutionRequestHandler_AnswersHeadAsWellAsGet() {
        var (provider, _, services) = Build();

        Assert.NotNull(provider.GetExecutionRequestHandler(Context("/health/live", "HEAD")));
    }

    /// <summary>
    /// A write verb is not a health probe. Declining rather than answering leaves the path free for
    /// whatever else is registered.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public void GetExecutionRequestHandler_DeclinesWriteVerbs(string method) {
        var (provider, _, services) = Build();

        Assert.Null(provider.GetExecutionRequestHandler(Context("/health/live", method)));
    }

    [Fact]
    public void GetExecutionRequestHandler_DeclinesEverythingElse() {
        var (provider, _, services) = Build();

        Assert.Null(provider.GetExecutionRequestHandler(Context("/orders")));
        Assert.Null(provider.GetExecutionRequestHandler(Context("/health")));
    }

    [Fact]
    public void GetExecutionRequestHandler_HonoursConfiguredPaths() {
        var config = new HealthCheckConfiguration {
            LivePath = "/_alive", ReadyPath = "/_ready"
        };

        var (provider, _, services) = Build(config);

        Assert.NotNull(provider.GetExecutionRequestHandler(Context("/_alive")));
        Assert.NotNull(provider.GetExecutionRequestHandler(Context("/_ready")));
        Assert.Null(provider.GetExecutionRequestHandler(Context("/health/live")));
    }

    // -------------------------------------------------------------- response

    /// <summary>
    /// Never cached. A cached readiness answer is worse than none: it reports the state of the
    /// instance at some earlier moment, to a load balancer making a decision now.
    /// </summary>
    [Fact]
    public async Task Ready_IsNotCacheable() {
        var (provider, _, services) = Build();
        var context = Context("/health/ready");

        var match = provider.GetExecutionRequestHandler(context);

        await match!.Handler!.GetExecutionChain(context).Next();

        Assert.Equal("no-store", context.Response.Headers[KnownHeaders.CacheControl].ToString());
        Assert.Equal("application/json", context.Response.ContentType);
    }

    /// <summary>
    /// The body is written directly, so the response is answerable even when the serialization
    /// stack is itself what is broken - and nothing negotiates a representation for it.
    /// </summary>
    [Fact]
    public async Task Ready_DoesNotGoThroughSerialization() {
        var (provider, _, services) = Build();
        var context = Context("/health/ready");

        var match = provider.GetExecutionRequestHandler(context);

        await match!.Handler!.GetExecutionChain(context).Next();

        Assert.False(context.Response.ShouldSerialize);
        Assert.Null(context.Response.ResponseValue);
    }
}
