using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;

namespace Hardened.Benchmarks.Pipeline;

/// <summary>
/// What returning <c>Task</c> from the filter interfaces costs, against <c>ValueTask</c>.
///
/// <para>
/// <c>IExecutionFilter.Execute</c> and <c>IExecutionChain.Next</c> both return <c>Task</c>. A filter
/// that completes synchronously — which most do, most of the time — still has to hand back a
/// <c>Task</c>, and if it is written <c>async</c> it also boxes a state machine. <c>ValueTask</c>
/// lets that path return without allocating.
/// </para>
///
/// <para>
/// Changing the real interfaces means touching 72 implementations across three repositories, so
/// this measures the shape in isolation first: two chains that differ only in what they return,
/// walked the same way, so the difference is the awaitable and nothing else. Multiply the per-filter
/// figure by the chain length to size the real change.
/// </para>
///
/// <para>
/// Both variants mirror <c>ExecutionChain</c>: an index walked forwards, a filter list, and
/// <c>CompletedTask</c> at the end. The synchronous variants model a filter that does its work and
/// returns; the async ones model a filter written with <c>async</c>/<c>await</c> that happens to
/// complete without yielding, which is the common case and the expensive one.
/// </para>
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Pipeline)]
public class FilterAwaitableBenchmarks {

    [Params(3, 6, 12)]
    public int FilterCount { get; set; }

    private interface ITaskFilter {
        Task Execute(TaskChain chain);
    }

    private interface IValueTaskFilter {
        ValueTask Execute(ValueTaskChain chain);
    }

    private sealed class TaskChain {
        private readonly ITaskFilter[] _filters;
        private int _index;

        public TaskChain(ITaskFilter[] filters) => _filters = filters;

        public void Reset() => _index = 0;

        public Task Next() =>
            _index >= _filters.Length ? Task.CompletedTask : _filters[_index++].Execute(this);
    }

    private sealed class ValueTaskChain {
        private readonly IValueTaskFilter[] _filters;
        private int _index;

        public ValueTaskChain(IValueTaskFilter[] filters) => _filters = filters;

        public void Reset() => _index = 0;

        public ValueTask Next() =>
            _index >= _filters.Length ? ValueTask.CompletedTask : _filters[_index++].Execute(this);
    }

    /// <summary>Written <c>async</c>, completes without yielding — the common filter.</summary>
    private sealed class AsyncTaskFilter : ITaskFilter {
        public async Task Execute(TaskChain chain) {
            await chain.Next();
        }
    }

    private sealed class AsyncValueTaskFilter : IValueTaskFilter {
        public async ValueTask Execute(ValueTaskChain chain) {
            await chain.Next();
        }
    }

    /// <summary>Hand-written to avoid the state machine, for reference.</summary>
    private sealed class SyncTaskFilter : ITaskFilter {
        public Task Execute(TaskChain chain) => chain.Next();
    }

    private sealed class SyncValueTaskFilter : IValueTaskFilter {
        public ValueTask Execute(ValueTaskChain chain) => chain.Next();
    }

    private TaskChain _asyncTask = null!;
    private ValueTaskChain _asyncValueTask = null!;
    private TaskChain _syncTask = null!;
    private ValueTaskChain _syncValueTask = null!;

    [GlobalSetup]
    public void Setup() {
        _asyncTask = new TaskChain(Enumerable.Range(0, FilterCount)
            .Select(_ => (ITaskFilter)new AsyncTaskFilter()).ToArray());
        _asyncValueTask = new ValueTaskChain(Enumerable.Range(0, FilterCount)
            .Select(_ => (IValueTaskFilter)new AsyncValueTaskFilter()).ToArray());
        _syncTask = new TaskChain(Enumerable.Range(0, FilterCount)
            .Select(_ => (ITaskFilter)new SyncTaskFilter()).ToArray());
        _syncValueTask = new ValueTaskChain(Enumerable.Range(0, FilterCount)
            .Select(_ => (IValueTaskFilter)new SyncValueTaskFilter()).ToArray());
    }

    [Benchmark(Baseline = true)]
    public async Task AsyncFiltersReturningTask() {
        _asyncTask.Reset();
        await _asyncTask.Next();
    }

    [Benchmark]
    public async Task AsyncFiltersReturningValueTask() {
        _asyncValueTask.Reset();
        await _asyncValueTask.Next();
    }

    [Benchmark]
    public async Task SyncFiltersReturningTask() {
        _syncTask.Reset();
        await _syncTask.Next();
    }

    [Benchmark]
    public async Task SyncFiltersReturningValueTask() {
        _syncValueTask.Reset();
        await _syncValueTask.Next();
    }
}
