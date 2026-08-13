using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;

namespace Hardened.Benchmarks.Pipeline;

/// <summary>
/// Hardened against ASP.NET Core, at the one layer where the comparison is honest.
///
/// Every pipeline here is entered the same way: a hand-built <c>HttpContext</c> handed to a
/// <c>RequestDelegate</c>, with no server underneath. Route matching, binding, deserialization,
/// handler invocation and serialization all run; sockets, HTTP parsing and framing do not.
/// Including those would measure a Kestrel deployment rather than a pipeline, which is the wrong
/// question for a framework that also runs on Lambda — and their variance alone would swamp the
/// differences being measured.
///
/// Both ASP.NET flavors appear twice. Minimal APIs and MVC default to reflection-based
/// System.Text.Json while Hardened uses source-generated <c>JsonTypeInfo</c>, so comparing only
/// the defaults would report a serializer difference as a pipeline difference. The
/// <c>SourceGenJson</c> variants remove that confound; the reflection variants are what an
/// unconfigured ASP.NET app actually runs.
///
/// Minimal API is the fairer comparison — like Hardened it compiles a delegate per route ahead
/// of time. MVC is the more commonly deployed one, and carries per-request machinery (action
/// descriptors, the filter pipeline, model binding, result execution) that neither of the others
/// has an equivalent for.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.AspNet)]
public class FrameworkComparisonBenchmarks {
    private HardenedNativeHarness _hardenedNative = null!;
    private HardenedFeatureHarness _hardenedFeatures = null!;
    private HardenedAspNetHarness _hardenedAspNet = null!;
    private AspNetHarness _minimalApi = null!;
    private AspNetHarness _minimalApiSourceGen = null!;
    private AspNetHarness _mvc = null!;
    private AspNetHarness _mvcSourceGen = null!;
    private MemoryStream _responseBody = null!;

    public IEnumerable<RequestScenario> ScenarioValues => Scenarios.All;

    [ParamsSource(nameof(ScenarioValues))]
    public RequestScenario Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup() {
        _hardenedNative = new HardenedNativeHarness();
        _hardenedFeatures = new HardenedFeatureHarness();
        _hardenedAspNet = new HardenedAspNetHarness();
        _minimalApi = new AspNetHarness(AspNetFlavor.MinimalApi, sourceGeneratedJson: false);
        _minimalApiSourceGen = new AspNetHarness(AspNetFlavor.MinimalApi, sourceGeneratedJson: true);
        _mvc = new AspNetHarness(AspNetFlavor.Mvc, sourceGeneratedJson: false);
        _mvcSourceGen = new AspNetHarness(AspNetFlavor.Mvc, sourceGeneratedJson: true);
        _responseBody = new MemoryStream();

        foreach (var harness in new IPipelineHarness[] {
            _hardenedNative, _hardenedFeatures, _hardenedAspNet,
            _minimalApi, _minimalApiSourceGen, _mvc, _mvcSourceGen
        }) {
            _responseBody.SetLength(0);
            var status = harness.Execute(Scenario, _responseBody).GetAwaiter().GetResult();

            if (status != 200 || _responseBody.Length == 0) {
                throw new InvalidOperationException(
                    $"{harness.Name} returned {status} with a {_responseBody.Length} byte body " +
                    $"for {Scenario.Name}. A pipeline that is not handling the route would be " +
                    "timed as though it were.");
            }
        }
    }

    [Benchmark(Baseline = true), BenchmarkCategory(BenchmarkCategories.AspNet)]
    public async Task<int> Hardened() {
        _responseBody.SetLength(0);

        return await _hardenedNative.Execute(Scenario, _responseBody);
    }

    /// <summary>
    /// Hardened on a server's feature collection, owning <c>IHttpApplication</c> rather than
    /// running as ASP.NET middleware. This is the one that would run on Kestrel without the
    /// ASP.NET pipeline in front of it.
    /// </summary>
    [Benchmark]
    public async Task<int> HardenedOnServerFeatures() {
        _responseBody.SetLength(0);

        return await _hardenedFeatures.Execute(Scenario, _responseBody);
    }

    [Benchmark]
    public async Task<int> HardenedOnAspNet() {
        _responseBody.SetLength(0);

        return await _hardenedAspNet.Execute(Scenario, _responseBody);
    }

    [Benchmark]
    public async Task<int> AspNetMinimalApi() {
        _responseBody.SetLength(0);

        return await _minimalApi.Execute(Scenario, _responseBody);
    }

    [Benchmark]
    public async Task<int> AspNetMinimalApiSourceGenJson() {
        _responseBody.SetLength(0);

        return await _minimalApiSourceGen.Execute(Scenario, _responseBody);
    }

    [Benchmark]
    public async Task<int> AspNetMvc() {
        _responseBody.SetLength(0);

        return await _mvc.Execute(Scenario, _responseBody);
    }

    [Benchmark]
    public async Task<int> AspNetMvcSourceGenJson() {
        _responseBody.SetLength(0);

        return await _mvcSourceGen.Execute(Scenario, _responseBody);
    }

    [GlobalCleanup]
    public void Cleanup() {
        _hardenedNative.Dispose();
        _hardenedFeatures.Dispose();
        _hardenedAspNet.Dispose();
        _minimalApi.Dispose();
        _minimalApiSourceGen.Dispose();
        _mvc.Dispose();
        _mvcSourceGen.Dispose();
        _responseBody.Dispose();
    }
}
