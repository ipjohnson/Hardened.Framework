using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;

namespace Hardened.Benchmarks.Micro;

/// <summary>
/// String-to-type conversion, the step every path token, query value and header goes through.
///
/// <c>StringConverterService</c> checks a dictionary of registered converters first and falls
/// back to a chain of <c>typeof(T) ==</c> comparisons. The JIT collapses that chain for a
/// concrete <c>T</c>, so the interesting question is what the dictionary probe costs on the way
/// to it — which is why the baseline here is the same parse done directly.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Micro)]
public class StringConversionBenchmarks {
    private StringConverterService _converter = null!;

    [GlobalSetup]
    public void Setup() {
        // No custom converters registered: the default configuration, and the path that always
        // falls through the dictionary to the built-in conversions.
        _converter = new StringConverterService(Array.Empty<IStringConverter>());
    }

    [Benchmark(Baseline = true)]
    public int ParseIntDirect() => int.Parse("12345");

    [Benchmark]
    public int ParseInt() => _converter.ParseRequired<int>("12345", "value");

    [Benchmark]
    public long ParseLong() => _converter.ParseRequired<long>("123456789012", "value");

    [Benchmark]
    public Guid ParseGuid() =>
        _converter.ParseRequired<Guid>("8f14e45f-ceea-467a-9f3a-1f0b7f1b3c2d", "value");

    [Benchmark]
    public DateTime ParseDateTime() => _converter.ParseRequired<DateTime>("2026-08-12", "value");

    /// <summary>The identity case — common, and worth knowing is not paying for a conversion.</summary>
    [Benchmark]
    public string ParseString() => _converter.ParseRequired<string>("benchmark", "value");

    /// <summary>Absent optional value, which short-circuits before any parsing.</summary>
    [Benchmark]
    public int ParseOptionalMissing() => _converter.ParseOptional<int>("", "value");
}
