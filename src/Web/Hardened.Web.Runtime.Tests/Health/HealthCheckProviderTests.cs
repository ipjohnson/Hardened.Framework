using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
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

    private static (HealthCheckProvider Provider, HealthCheckConfiguration Config) Build(
        params IHealthCheck[] checks) =>
        Build(new HealthCheckConfiguration(), checks);

    private static (HealthCheckProvider, HealthCheckConfiguration) Build(
        HealthCheckConfiguration config, params IHealthCheck[] checks) {
        var services = new ServiceCollection();

        foreach (var check in checks) {
            services.AddSingleton(check);
        }

        var provider = services.BuildServiceProvider();

        return (new HealthCheckProvider(config, provider), config);
    }

    private static IExecutionContext Context(string path, string method = "GET") {
        var request = new TestExecutionRequest(
            method, path, "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>()));

        var services = new ServiceCollection().BuildServiceProvider();

        return new TestExecutionContext(
            services, services, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(new MemoryStream()), CancellationToken.None);
    }

    private static async Task<(int? Status, JsonElement Body)> Probe(
        HealthCheckProvider provider, string path, string method = "GET") {
        var context = Context(path, method);
        var match = provider.GetExecutionRequestHandler(context);

        Assert.NotNull(match);
        Assert.NotNull(match!.Handler);

        await match.Handler!.GetExecutionChain(context).Next();

        var json = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

        return (context.Response.Status, JsonDocument.Parse(json).RootElement);
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
        var (provider, _) = Build(unhealthy);

        var (status, body) = await Probe(provider, "/health/live");

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
        var (provider, _) = Build();

        var (status, body) = await Probe(provider, "/health/ready");

        Assert.Equal(200, status);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_RunsEveryRegisteredCheck() {
        var first = new Stub("a", HealthCheckResult.Healthy());
        var second = new Stub("b", HealthCheckResult.Healthy());

        var (provider, _) = Build(first, second);

        await Probe(provider, "/health/ready");

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
        var (provider, _) = Build(new Stub("only", new HealthCheckResult(status)));

        var (code, body) = await Probe(provider, "/health/ready");

        Assert.Equal(expected, code);
        Assert.Equal(status.ToString(), body.GetProperty("status").GetString());
    }

    /// <summary>The worst check decides the overall answer.</summary>
    [Fact]
    public async Task Ready_ReportsTheWorstOfTheChecks() {
        var (provider, _) = Build(
            new Stub("fine", HealthCheckResult.Healthy()),
            new Stub("slow", HealthCheckResult.Degraded()),
            new Stub("down", HealthCheckResult.Unhealthy()));

        var (status, body) = await Probe(provider, "/health/ready");

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
        var (provider, _) = Build(
            new Stub("explodes", _ => throw new InvalidOperationException("boom")));

        var (status, body) = await Probe(provider, "/health/ready");

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

        var (provider, _) = Build(
            config,
            new Stub("hangs", async token => {
                await Task.Delay(TimeSpan.FromSeconds(30), token);

                return HealthCheckResult.Healthy();
            }));

        var (status, _) = await Probe(provider, "/health/ready");

        Assert.Equal(503, status);
    }

    /// <summary>The check is handed a token that will actually be cancelled.</summary>
    [Fact]
    public async Task Ready_PassesACancellableTokenToTheCheck() {
        var config = new HealthCheckConfiguration { CheckTimeout = TimeSpan.FromMilliseconds(50) };
        var observed = CancellationToken.None;

        var (provider, _) = Build(
            config,
            new Stub("watches", token => {
                observed = token;

                return Task.FromResult(HealthCheckResult.Healthy());
            }));

        await Probe(provider, "/health/ready");

        Assert.True(observed.CanBeCanceled);
    }

    // --------------------------------------------------------------- detail

    /// <summary>
    /// Status only by default. Readiness is unauthenticated by construction, so a body enumerating
    /// dependency names and error strings hands an anonymous caller a map of the system.
    /// </summary>
    [Fact]
    public async Task Ready_ReportsNoDetailByDefault() {
        var (provider, _) = Build(
            new Stub("internal-billing-db", HealthCheckResult.Unhealthy("connection refused")));

        var (_, body) = await Probe(provider, "/health/ready");

        Assert.False(body.TryGetProperty("checks", out _));
    }

    [Fact]
    public async Task Ready_ReportsPerCheckDetailWhenAsked() {
        var config = new HealthCheckConfiguration { IncludeDetail = true };

        var (provider, _) = Build(
            config,
            new Stub("db", HealthCheckResult.Unhealthy("connection refused")),
            new Stub("cache", HealthCheckResult.Healthy()));

        var (_, body) = await Probe(provider, "/health/ready");

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
        var (provider, _) = Build();

        Assert.NotNull(provider.GetExecutionRequestHandler(Context(path)));
    }

    /// <summary>A probe issuing HEAD is asking the same question.</summary>
    [Fact]
    public void GetExecutionRequestHandler_AnswersHeadAsWellAsGet() {
        var (provider, _) = Build();

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
        var (provider, _) = Build();

        Assert.Null(provider.GetExecutionRequestHandler(Context("/health/live", method)));
    }

    [Fact]
    public void GetExecutionRequestHandler_DeclinesEverythingElse() {
        var (provider, _) = Build();

        Assert.Null(provider.GetExecutionRequestHandler(Context("/orders")));
        Assert.Null(provider.GetExecutionRequestHandler(Context("/health")));
    }

    [Fact]
    public void GetExecutionRequestHandler_HonoursConfiguredPaths() {
        var config = new HealthCheckConfiguration {
            LivePath = "/_alive", ReadyPath = "/_ready"
        };

        var (provider, _) = Build(config);

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
        var (provider, _) = Build();
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
        var (provider, _) = Build();
        var context = Context("/health/ready");

        var match = provider.GetExecutionRequestHandler(context);

        await match!.Handler!.GetExecutionChain(context).Next();

        Assert.False(context.Response.ShouldSerialize);
        Assert.Null(context.Response.ResponseValue);
    }
}
