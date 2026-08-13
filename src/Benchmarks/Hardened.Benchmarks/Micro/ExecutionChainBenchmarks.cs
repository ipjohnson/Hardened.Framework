using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Benchmarks.Micro;

/// <summary>
/// The filter chain itself, with the filters doing nothing.
///
/// <c>ExecutionChain</c> holds an index and walks a list of factory delegates, invoking each
/// filter's <c>Execute</c> with itself as the continuation. Every filter a route carries — CORS,
/// validation, retry, the handler invoke — is one step of this walk, so the per-step cost sets a
/// floor under any route's overhead that scales with how many filters it has.
///
/// The filters here return a completed task, so what is measured is the walk and the state
/// machine around it rather than any work inside a filter.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Micro)]
public class ExecutionChainBenchmarks {
    private HardenedNativeHarness _harness = null!;
    private IServiceScope _scope = null!;
    private MemoryStream _responseBody = null!;
    private IExecutionContext _context = null!;
    private Func<IExecutionContext, IExecutionFilter>[] _filters = null!;

    /// <summary>Chain depths spanning a bare route through a heavily filtered one.</summary>
    [Params(1, 4, 8, 16)]
    public int FilterCount { get; set; }

    [GlobalSetup]
    public void Setup() {
        _harness = new HardenedNativeHarness();
        _scope = _harness.CreateScope();
        _responseBody = new MemoryStream();
        _context = _harness.CreateContext(Scenarios.Item, _scope, _responseBody);

        var passThrough = new PassThroughFilter();

        _filters = Enumerable
            .Range(0, FilterCount)
            .Select<int, Func<IExecutionContext, IExecutionFilter>>(_ => _ => passThrough)
            .ToArray();
    }

    [Benchmark]
    public async Task Walk() {
        await new ExecutionChain(_filters, _context).Next();
    }

    /// <summary>
    /// Fork copies the chain at its current index, which forked filters do to run a nested chain
    /// over a modified context. Measured separately because it allocates a second chain.
    /// </summary>
    [Benchmark]
    public async Task WalkWithFork() {
        var chain = new ExecutionChain(_filters, _context);

        await chain.Fork(_context).Next();
    }

    private sealed class PassThroughFilter : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    [GlobalCleanup]
    public void Cleanup() {
        _scope.Dispose();
        _harness.Dispose();
        _responseBody.Dispose();
    }
}
