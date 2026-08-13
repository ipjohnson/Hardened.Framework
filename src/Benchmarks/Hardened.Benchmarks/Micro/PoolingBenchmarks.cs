using System.Text;
using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Benchmarks.Micro;

/// <summary>
/// The object pools, against simply allocating.
///
/// <c>ItemPool</c> is a lock-free stack using interlocked compare-and-swap, so renting is not
/// free — the question a pool has to answer is whether it beats the allocation plus the GC work
/// it avoids. These benchmarks put the two side by side. The allocating variants will usually
/// look competitive on mean time while losing badly on the allocation column, which is the
/// column that matters here: the pools exist to keep steady-state requests out of Gen0, not to
/// make any single rent faster.
///
/// The contended variant is included because the uncontended CAS path is the best case, and a
/// pool shared across concurrent requests does not get the best case.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Micro)]
public class PoolingBenchmarks {
    private StringBuilderPool _stringBuilderPool = null!;
    private MemoryStreamPool _memoryStreamPool = null!;

    [GlobalSetup]
    public void Setup() {
        _stringBuilderPool = new StringBuilderPool(256);
        _memoryStreamPool = new MemoryStreamPool();

        // Prime both pools so the first measured rent is a hit rather than a miss.
        using (var reservation = _stringBuilderPool.Get()) {
            reservation.Item.Append("prime");
        }

        using (var reservation = _memoryStreamPool.Get()) {
            reservation.Item.WriteByte(1);
        }
    }

    [Benchmark(Baseline = true)]
    public int StringBuilderPooled() {
        using var reservation = _stringBuilderPool.Get();

        reservation.Item.Append("benchmark");

        return reservation.Item.Length;
    }

    [Benchmark]
    public int StringBuilderAllocated() {
        var builder = new StringBuilder(256);

        builder.Append("benchmark");

        return builder.Length;
    }

    [Benchmark]
    public long MemoryStreamPooled() {
        using var reservation = _memoryStreamPool.Get();

        reservation.Item.WriteByte(42);

        return reservation.Item.Length;
    }

    [Benchmark]
    public long MemoryStreamAllocated() {
        using var stream = new MemoryStream(1024);

        stream.WriteByte(42);

        return stream.Length;
    }

    /// <summary>
    /// Four rents outstanding at once, so the CAS loop has to walk past a non-empty list rather
    /// than taking the uncontended head every time.
    /// </summary>
    [Benchmark]
    public int StringBuilderPooledNested() {
        using var first = _stringBuilderPool.Get();
        using var second = _stringBuilderPool.Get();
        using var third = _stringBuilderPool.Get();
        using var fourth = _stringBuilderPool.Get();

        first.Item.Append("benchmark");

        return first.Item.Length + second.Item.Length + third.Item.Length + fourth.Item.Length;
    }

    [GlobalCleanup]
    public void Cleanup() {
        _memoryStreamPool.Dispose();
    }
}
