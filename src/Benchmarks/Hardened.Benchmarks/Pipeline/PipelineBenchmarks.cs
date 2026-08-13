using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;

namespace Hardened.Benchmarks.Pipeline;

/// <summary>
/// A whole request through Hardened, in both of its deployments.
///
/// This is the default pipeline measurement and the one to watch for regressions.
/// <c>HardenedNative</c> is the floor — an execution context handed straight to the middleware
/// chain, which is how Hardened runs on Lambda and on any other non-ASP.NET compute.
/// <c>HardenedOnAspNet</c> runs the identical chain through <c>UseHardened</c>, so the gap
/// between the two is the cost of <c>AspNetCoreRequestHandler</c> and
/// <c>AspNetExecutionContext</c> and nothing else.
///
/// Both include DI scope creation and context construction, because a transport adapter pays
/// for those on every request. <see cref="ContextConstructionBenchmarks"/> measures that portion
/// on its own so it can be subtracted rather than guessed at.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Pipeline)]
public class PipelineBenchmarks {
    private HardenedNativeHarness _native = null!;
    private HardenedFeatureHarness _features = null!;
    private HardenedAspNetHarness _aspNet = null!;
    private MemoryStream _responseBody = null!;

    public IEnumerable<RequestScenario> ScenarioValues => Scenarios.All;

    [ParamsSource(nameof(ScenarioValues))]
    public RequestScenario Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup() {
        _native = new HardenedNativeHarness();
        _features = new HardenedFeatureHarness();
        _aspNet = new HardenedAspNetHarness();
        _responseBody = new MemoryStream();

        // A route that fails to match still completes, and does so faster than one that matches.
        // Program verifies this too, but a --no-verify run would otherwise report a very
        // attractive number for producing a 404.
        AssertHandled(_native.Execute(Scenario, _responseBody).GetAwaiter().GetResult(), "native");
        _responseBody.SetLength(0);
        AssertHandled(_features.Execute(Scenario, _responseBody).GetAwaiter().GetResult(), "features");
        _responseBody.SetLength(0);
        AssertHandled(_aspNet.Execute(Scenario, _responseBody).GetAwaiter().GetResult(), "asp.net");
    }

    private void AssertHandled(int status, string pipeline) {
        if (status != 200 || _responseBody.Length == 0) {
            throw new InvalidOperationException(
                $"{pipeline} returned {status} with a {_responseBody.Length} byte body for " +
                $"{Scenario.Name}. The route is not being handled, so any timing would be noise.");
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<int> HardenedNative() {
        _responseBody.SetLength(0);

        return await _native.Execute(Scenario, _responseBody);
    }

    /// <summary>
    /// Hardened owning <c>IHttpApplication</c> directly — Kestrel's contract, without
    /// <c>HostingApplication</c>, <c>HttpContext</c> or the ASP.NET middleware pipeline. See
    /// <see cref="HardenedHttpApplication"/>.
    /// </summary>
    [Benchmark]
    public async Task<int> HardenedOnServerFeatures() {
        _responseBody.SetLength(0);

        return await _features.Execute(Scenario, _responseBody);
    }

    [Benchmark]
    public async Task<int> HardenedOnAspNet() {
        _responseBody.SetLength(0);

        return await _aspNet.Execute(Scenario, _responseBody);
    }

    [GlobalCleanup]
    public void Cleanup() {
        _native.Dispose();
        _features.Dispose();
        _aspNet.Dispose();
        _responseBody.Dispose();
    }
}
