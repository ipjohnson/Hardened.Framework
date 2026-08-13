using Hardened.IntegrationTests.Benchmark.SUT.Models;
using Hardened.IntegrationTests.Benchmark.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.Benchmark.SUT;

/// <summary>
/// The six TechEmpower test types over <see cref="BenchmarkData"/>.
/// </summary>
/// <remarks>
/// Nothing here mentions a content type or a view. <c>/plaintext</c> returns a string and
/// <c>/fortunes</c> returns a model; the spec says one is text/plain and the other renders through
/// the Fortunes template, and the generated handler carries both. That separation is most of what
/// this fixture exists to prove.
/// </remarks>
[Handler]
public class BenchmarkServiceImpl : IBenchmarkService {
    private readonly BenchmarkData _data;

    public BenchmarkServiceImpl(BenchmarkData data) {
        _data = data;
    }

    public Task<HelloMessage> JsonSerialization() =>
        Task.FromResult(new HelloMessage("Hello, World!"));

    public Task<string> PlainText() =>
        Task.FromResult("Hello, World!");

    public Task<World> SingleQuery() =>
        Task.FromResult(_data.Random());

    public Task<List<World>> MultipleQueries(string? queries) {
        var count = BenchmarkData.QueryCount(queries);

        return Task.FromResult(Enumerable.Range(0, count).Select(_ => _data.Random()).ToList());
    }

    public Task<List<World>> DatabaseUpdates(string? queries) {
        var count = BenchmarkData.QueryCount(queries);

        return Task.FromResult(Enumerable.Range(0, count).Select(_ => _data.UpdateRandom()).ToList());
    }

    /// <summary>
    /// The benchmark adds one fortune at request time and sorts the whole set by message text, so
    /// the added row lands in the middle rather than at the end.
    /// </summary>
    public Task<FortunePage> Fortunes() {
        var fortunes = _data.Fortunes
            .Append(new Fortune(0, "Additional fortune added at request time."))
            .OrderBy(fortune => fortune.Message, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(new FortunePage(fortunes));
    }
}
