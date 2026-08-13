using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Benchmarks.Micro;

/// <summary>
/// Route matching on its own — the generated matcher, without binding, invocation or
/// serialization.
///
/// The Web source generator emits a nested set of span comparisons and switch statements over the
/// path rather than a runtime lookup table, so this is measuring generated code specific to the
/// route set it was compiled against. That is worth isolating: when a pipeline number moves, this
/// answers whether the matcher moved with it.
///
/// The context is built once in setup. Matching is a pure read of it, so rebuilding it per
/// iteration would measure context construction instead — which
/// <c>ContextConstructionBenchmarks</c> already covers separately.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Micro)]
public class RoutingBenchmarks {
    private HardenedNativeHarness _harness = null!;
    private IServiceScope _scope = null!;
    private MemoryStream _responseBody = null!;
    private IWebExecutionRequestHandlerProvider[] _providers = null!;
    private Hardened.Requests.Abstract.Execution.IExecutionContext _context = null!;

    public IEnumerable<RequestScenario> ScenarioValues => Scenarios.All;

    [ParamsSource(nameof(ScenarioValues))]
    public RequestScenario Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup() {
        _harness = new HardenedNativeHarness();
        _scope = _harness.CreateScope();
        _responseBody = new MemoryStream();
        _context = _harness.CreateContext(Scenario, _scope, _responseBody);

        // WebExecutionHandlerService reverses the registration order before walking them, so the
        // same order is used here rather than the raw enumeration order.
        _providers = _harness.Provider
            .GetServices<IWebExecutionRequestHandlerProvider>()
            .Reverse()
            .ToArray();

        if (Match() is null) {
            throw new InvalidOperationException(
                $"No handler matched {Scenario.Name}. Timing a failed match measures how fast " +
                "the matcher gives up, not how fast it routes.");
        }
    }

    [Benchmark]
    public object? Match() {
        foreach (var provider in _providers) {
            var handler = provider.GetExecutionRequestHandler(_context);

            if (handler != null) {
                return handler;
            }
        }

        return null;
    }

    [GlobalCleanup]
    public void Cleanup() {
        _scope.Dispose();
        _harness.Dispose();
        _responseBody.Dispose();
    }
}
