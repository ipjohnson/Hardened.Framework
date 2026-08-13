using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Benchmarks.Pipeline;

/// <summary>
/// The per-request setup that happens before any pipeline runs: creating a DI scope, and building
/// the context object the pipeline is handed.
///
/// The pipeline benchmarks include this work, because something pays for it on every real
/// request. Measuring it separately means the reader can subtract it instead of guessing at it,
/// and it makes one asymmetry visible rather than hidden: Kestrel pools and resets its feature
/// collection across a connection, whereas the harness builds a fresh one per request. Hardened
/// allocates its context per request in production too, so the native number here is what
/// production pays, while the ASP.NET number is a slight overstatement of what Kestrel would.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Pipeline)]
public class ContextConstructionBenchmarks {
    private HardenedNativeHarness _native = null!;
    private HardenedAspNetHarness _aspNet = null!;
    private MemoryStream _responseBody = null!;

    public IEnumerable<RequestScenario> ScenarioValues => [Scenarios.Item, Scenarios.Sum];

    [ParamsSource(nameof(ScenarioValues))]
    public RequestScenario Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup() {
        _native = new HardenedNativeHarness();
        _aspNet = new HardenedAspNetHarness();
        _responseBody = new MemoryStream();
    }

    /// <summary>Scope creation alone, common to every pipeline.</summary>
    [Benchmark(Baseline = true)]
    public int ScopeOnly() {
        using var scope = _native.CreateScope();

        return scope.ServiceProvider.GetHashCode();
    }

    [Benchmark]
    public object HardenedContext() {
        using var scope = _native.CreateScope();

        return _native.CreateContext(Scenario, scope, _responseBody);
    }

    [Benchmark]
    public object AspNetHttpContext() {
        using var scope = _aspNet.Provider.CreateScope();

        return HttpContextFactory.Create(Scenario, scope.ServiceProvider, _responseBody);
    }

    [GlobalCleanup]
    public void Cleanup() {
        _native.Dispose();
        _aspNet.Dispose();
        _responseBody.Dispose();
    }
}
