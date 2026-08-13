using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Contracts;
using Hardened.Benchmarks.Infrastructure;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Benchmarks.Micro;

/// <summary>
/// Hardened's JSON serializer against System.Text.Json used directly.
///
/// Hardened resolves <c>IJsonSerializer</c> from DI and configures its options from the
/// registered source-generated contexts, so this measures that configured path rather than a
/// bare <c>JsonSerializer</c> call. The raw System.Text.Json benchmarks are the floor: they show
/// what the serialization would cost with no framework involved, which is the right way to read
/// how much of a pipeline number is serialization and how much is everything else.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Micro)]
public class SerializationBenchmarks {
    private HardenedNativeHarness _harness = null!;
    private IJsonSerializer _serializer = null!;
    private MemoryStream _target = null!;
    private byte[] _requestBytes = null!;
    private ItemResponse _response = null!;
    private JsonSerializerOptions _reflectionOptions = null!;
    private JsonSerializerOptions _sourceGenOptions = null!;

    [GlobalSetup]
    public void Setup() {
        _harness = new HardenedNativeHarness();
        _serializer = _harness.Provider.GetRequiredService<IJsonSerializer>();
        _target = new MemoryStream();

        _response = new ItemResponse {
            Id = 1,
            Name = "benchmark",
            Active = true,
            Score = 99.5
        };

        _requestBytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new SumRequest {
                Id = 7,
                Label = "benchmark",
                Values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
            }));

        _reflectionOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _sourceGenOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            TypeInfoResolver = BenchmarkJsonContext.Default
        };
    }

    [Benchmark(Baseline = true)]
    public async Task<long> HardenedSerialize() {
        _target.SetLength(0);
        await _serializer.SerializeAsync(_target, _response);

        return _target.Length;
    }

    [Benchmark]
    public async Task<SumRequest> HardenedDeserialize() {
        using var source = new MemoryStream(_requestBytes, false);

        return await _serializer.DeserializeAsync<SumRequest>(source);
    }

    [Benchmark]
    public async Task<long> SystemTextJsonSerialize() {
        _target.SetLength(0);
        await JsonSerializer.SerializeAsync(_target, _response, _reflectionOptions);

        return _target.Length;
    }

    [Benchmark]
    public async Task<long> SystemTextJsonSerializeSourceGen() {
        _target.SetLength(0);
        await JsonSerializer.SerializeAsync(_target, _response, _sourceGenOptions);

        return _target.Length;
    }

    [Benchmark]
    public async Task<SumRequest?> SystemTextJsonDeserialize() {
        using var source = new MemoryStream(_requestBytes, false);

        return await JsonSerializer.DeserializeAsync<SumRequest>(source, _reflectionOptions);
    }

    [Benchmark]
    public async Task<SumRequest?> SystemTextJsonDeserializeSourceGen() {
        using var source = new MemoryStream(_requestBytes, false);

        return await JsonSerializer.DeserializeAsync<SumRequest>(source, _sourceGenOptions);
    }

    [GlobalCleanup]
    public void Cleanup() {
        _harness.Dispose();
        _target.Dispose();
    }
}
