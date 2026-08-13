using DependencyModules.Runtime.Attributes;
using Hardened.IntegrationTests.Benchmark.SUT.Models;

namespace Hardened.IntegrationTests.Benchmark.SUT;

/// <summary>
/// Stands in for the World and Fortune tables.
/// </summary>
/// <remarks>
/// In memory on purpose. The benchmark's database access is the part of TechEmpower this fixture is
/// not trying to measure - what is under test here is the generated routing, binding, serialization
/// and template rendering around it, and a real Npgsql dependency would make the suite need a
/// database to say anything about those.
/// </remarks>
[SingletonService]
public class BenchmarkData {
    /// <summary>The World table is 10,000 rows in the benchmark, with ids 1..10000.</summary>
    private const int WorldRowCount = 10_000;

    private readonly World[] _worlds;
    private readonly Random _random = new(20260813);

    public BenchmarkData() {
        _worlds = new World[WorldRowCount];

        for (var i = 0; i < WorldRowCount; i++) {
            _worlds[i] = new World(i + 1, (i * 7 % WorldRowCount) + 1);
        }
    }

    public World Random() {
        lock (_random) {
            return _worlds[_random.Next(WorldRowCount)];
        }
    }

    public World UpdateRandom() {
        lock (_random) {
            var index = _random.Next(WorldRowCount);
            var updated = _worlds[index] with { RandomNumber = _random.Next(WorldRowCount) + 1 };

            _worlds[index] = updated;

            return updated;
        }
    }

    public World? ById(int id) =>
        id >= 1 && id <= WorldRowCount ? _worlds[id - 1] : null;

    /// <summary>
    /// The twelve rows the benchmark seeds the Fortune table with, verbatim. The eleventh is the
    /// point of the test type: it has to reach the browser escaped.
    /// </summary>
    public IReadOnlyList<Fortune> Fortunes { get; } = [
        new Fortune(1, "fortune: No such file or directory"),
        new Fortune(2, "A computer scientist is someone who fixes things that aren't broken."),
        new Fortune(3, "After enough decimal places, nobody gives a damn."),
        new Fortune(4, "A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1"),
        new Fortune(5, "A computer program does what you tell it to do, not what you want it to do."),
        new Fortune(6, "Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen"),
        new Fortune(7, "Any program that runs right is obsolete."),
        new Fortune(8, "A list is only as strong as its weakest link. — Donald Knuth"),
        new Fortune(9, "Feature: A bug with seniority."),
        new Fortune(10, "Computers make very fast, very accurate mistakes."),
        new Fortune(11, "<script>alert(\"This should not be displayed in a browser alert box.\");</script>"),
        new Fortune(12, "フレームワークのベンチマーク")
    ];

    /// <summary>
    /// The benchmark's rule for the <c>queries</c> parameter: anything that is not an integer
    /// becomes 1, and the count is clamped to 1..500. It is declared as a string in the spec
    /// precisely so that "foo" reaches here rather than failing to bind.
    /// </summary>
    public static int QueryCount(string? queries) {
        if (!int.TryParse(queries, out var count)) {
            return 1;
        }

        return Math.Clamp(count, 1, 500);
    }
}
