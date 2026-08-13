using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;

namespace Hardened.Benchmarks.Startup;

/// <summary>
/// Application construction, and the cost of the first request through a cold application.
///
/// This is the number that matters on Lambda, where a cold start is charged to a real request's
/// latency rather than amortized over a long-lived process. It is also the one most likely to
/// regress invisibly: adding a module or a startup service costs nothing measurable per request
/// and shows up entirely here.
///
/// <c>FirstRequest</c> covers construction plus one request, so the difference between it and
/// <c>BuildAndStart</c> is what the first request pays over a steady-state one — JIT of the
/// generated handler, the serializer resolving its type info, and any lazily built state in the
/// route matcher.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Startup)]
public class StartupBenchmarks {

    /// <summary>Service collection populated and provider built, nothing resolved yet.</summary>
    [Benchmark(Baseline = true)]
    public object BuildProvider() {
        using var provider = HardenedAppFactory.BuildProvider();

        return provider;
    }

    /// <summary>Adds the startup services — the filter registry and CORS populate here.</summary>
    [Benchmark]
    public object BuildAndStart() {
        using var provider = HardenedAppFactory.BuildProvider();

        HardenedAppFactory.RunStartup(provider);

        return provider;
    }

    /// <summary>Construction through to a served response, which is what a cold start costs.</summary>
    [Benchmark]
    public async Task<int> FirstRequest() {
        using var harness = new HardenedNativeHarness();
        using var responseBody = new MemoryStream();

        return await harness.Execute(Scenarios.Item, responseBody);
    }

    /// <summary>The same, behind ASP.NET's adapter.</summary>
    [Benchmark]
    public async Task<int> FirstRequestOnAspNet() {
        using var harness = new HardenedAspNetHarness();
        using var responseBody = new MemoryStream();

        return await harness.Execute(Scenarios.Item, responseBody);
    }
}
