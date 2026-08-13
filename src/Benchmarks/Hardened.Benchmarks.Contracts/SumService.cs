namespace Hardened.Benchmarks.Contracts;

public interface ISumService {
    int Sum(IReadOnlyList<int> values);
}

/// <summary>
/// A deliberately trivial injected dependency, shared by every implementation under benchmark.
///
/// Its job is to make handler invocation include one scoped service resolution, which is what a
/// real handler does. The work inside it is near-free on purpose — anything substantial would be
/// added equally to all four pipelines and would shrink the relative differences being measured.
///
/// It carries no DI attributes and lives here rather than in either SUT so that both sides
/// register the identical type through the identical mechanism (<c>AddTransient</c>), leaving
/// resolution cost the same on both sides of the comparison.
/// </summary>
public class SumService : ISumService {
    public int Sum(IReadOnlyList<int> values) {
        var total = 0;

        for (var i = 0; i < values.Count; i++) {
            total += values[i];
        }

        return total;
    }
}
